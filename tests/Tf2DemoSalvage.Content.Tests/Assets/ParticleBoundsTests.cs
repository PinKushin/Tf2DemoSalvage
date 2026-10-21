using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// An effect's world box, which the leaf system files and collates it by (finding 84, D1): the particles, each grown by
/// its radius, children included, bloated to contain control point 0 (`BloatBoundsUsingControlPoint`,
/// particles_new.cpp:353); an effect with no particles is its control point alone (:354-357).
/// </summary>
/// <remarks>
/// The per-particle growth is not in published source (the bounds are computed inside the closed `particles.lib`): the
/// radius is interpolated, and finding 84 says so.
/// </remarks>
[TestFixture]
public sealed class ParticleBoundsTests
{
    [Test]
    public void Bounds_AParticleLeftBehindByItsPoint_ContainsTheParticleAndThePoint()
    {
        ParticleEffect effect = new(Once());
        effect.Step(Point(new Vector3(10f, 0f, 0f)), 1f / 66f);
        effect.SetControlPoint(0, Point(new Vector3(50f, 0f, 0f)));

        Vector3 at = effect.Particles.PositionOf(0);
        float radius = effect.Particles.RadiusOf(0);
        (Vector3 min, Vector3 max) = effect.Bounds();

        min.ShouldBe(at - new Vector3(radius));
        max.ShouldBe(Vector3.Max(at + new Vector3(radius), new Vector3(50f, 0f, 0f)));
    }

    [Test]
    public void Bounds_NoParticles_IsTheControlPointAlone()
    {
        ParticleEffect effect = new(Once() with { Emitters = [] });
        effect.SetControlPoint(0, Point(new Vector3(5f, 6f, 7f)));

        effect.Bounds().ShouldBe((new Vector3(5f, 6f, 7f), new Vector3(5f, 6f, 7f)));
    }

    private static ParticleControlPoint Point(Vector3 at) => new(at, Vector3.UnitX, Vector3.UnitY, Vector3.UnitZ);

    private static ParticleSystem Once() => new(
        Name: "test_once",
        Emitters:
        [
            new ParticleFunction("emit_instantaneously", "emit", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["num_to_emit"] = new(DmxAttributeType.Whole, 1d),
            }),
        ],
        Initializers:
        [
            new ParticleFunction("Radius Random", "radius", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["radius_min"] = new(DmxAttributeType.Real, 4d),
                ["radius_max"] = new(DmxAttributeType.Real, 4d),
            }),
        ],
        Operators: [],
        Renderers: [],
        Children: [],
        Parameters: new Dictionary<string, DmxValue>(StringComparer.Ordinal));
}
