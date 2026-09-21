using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// `C_OP_ConstrainDistanceToPath::EnforceConstraint` and `CParticleCollection::CalculatePathValues`, read out of
/// `particles.lib` (B396): what turns a medigun's particles into a beam.
/// </summary>
public sealed class PathConstraintConformanceTests
{
    private static readonly ParticleControlPoint Start = new(Vector3.Zero, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);
    private static readonly ParticleControlPoint End = new(new Vector3(100f, 0f, 0f), Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

    [Test]
    public void PathValues_NoBulge_IsTheMidpointBetweenTheControlPoints()
    {
        // `mid = start + ( end − start ) · mid point position`; a random bulge of 0 adds nothing.
        (Vector3 start, Vector3 mid, Vector3 end) = PathConstraint.PathValues(Function(Path(0.25f)), [Start, End]);

        start.ShouldBe(Vector3.Zero);
        mid.ShouldBe(new Vector3(25f, 0f, 0f));
        end.ShouldBe(new Vector3(100f, 0f, 0f));
    }

    [Test]
    public void PathValues_BulgeAlongTheStartsOrientation_IsScaledByHowFarOffTheLineItPoints()
    {
        // Bulge control 1: `mid += fwd · ( |end − start| · bulge · ( 1 − |dir · fwd| ) / |fwd| )` — a forward square to
        // the line bulges fully, one along it not at all.
        ParticleControlPoint up = Start with { Forward = Vector3.UnitZ };
        Dictionary<string, DmxValue> parameters = Path(0.5f);

        parameters["random bulge"] = new DmxValue(DmxAttributeType.Real, Number: 0.2d);
        parameters["bulge control 0=random 1=orientation of start pnt 2=orientation of end point"] =
            new DmxValue(DmxAttributeType.Whole, Number: 1d);

        PathConstraint.PathValues(Function(parameters), [up, End]).Mid.ShouldBe(new Vector3(50f, 0f, 20f));
        PathConstraint.PathValues(Function(parameters), [Start, End]).Mid.ShouldBe(new Vector3(50f, 0f, 0f));
    }

    [Test]
    public void Enforce_AParticleTooFarFromItsPathPoint_IsPulledToTheMaximum()
    {
        // Half way through a 10-second travel, the path point is ( 50, 0, 0 ); 20 units off it with a maximum of 10 puts
        // it 10 off, along the same line.
        ParticleStore particles = Particle(new Vector3(50f, 20f, 0f), age: 5f);
        Dictionary<string, DmxValue> parameters = Path(0.5f);

        parameters["maximum distance"] = new DmxValue(DmxAttributeType.Real, Number: 10d);

        PathConstraint.Enforce(particles, Function(parameters), [Start, End]).ShouldBeTrue();

        particles.PositionOf(0).X.ShouldBe(50f, 1e-3f);
        particles.PositionOf(0).Y.ShouldBe(10f, 1e-3f);
    }

    [Test]
    public void Enforce_AParticleTooNear_IsPushedOutToTheMinimum()
    {
        ParticleStore particles = Particle(new Vector3(50f, 1f, 0f), age: 5f);
        Dictionary<string, DmxValue> parameters = Path(0.5f);

        parameters["minimum distance"] = new DmxValue(DmxAttributeType.Real, Number: 5d);

        PathConstraint.Enforce(particles, Function(parameters), [Start, End]).ShouldBeTrue();

        particles.PositionOf(0).Y.ShouldBe(5f, 1e-3f);
    }

    [Test]
    public void Enforce_AParticleWithinBounds_IsLeftAlone()
    {
        ParticleStore particles = Particle(new Vector3(50f, 3f, 0f), age: 5f);

        PathConstraint.Enforce(particles, Function(Path(0.5f)), [Start, End]).ShouldBeFalse();

        particles.PositionOf(0).ShouldBe(new Vector3(50f, 3f, 0f));
    }

    [Test]
    public void Enforce_PastItsTravelTime_HoldsTheParticleAtTheEnd()
    {
        // `t = min( 1, age / travel time )`: a particle 20 seconds old on a 10-second path sits at the end.
        ParticleStore particles = Particle(new Vector3(100f, 40f, 0f), age: 20f);
        Dictionary<string, DmxValue> parameters = Path(0.5f);

        parameters["maximum distance"] = new DmxValue(DmxAttributeType.Real, Number: 0d);

        PathConstraint.Enforce(particles, Function(parameters), [Start, End]);

        particles.PositionOf(0).X.ShouldBe(100f, 1e-3f);
        particles.PositionOf(0).Y.ShouldBe(0f, 1e-3f);
    }

    private static ParticleStore Particle(Vector3 at, float age)
    {
        ParticleStore particles = new();

        particles.Add(at, lives: 100f);
        particles.Tick(age);

        return particles;
    }

    private static ParticleFunction Function(Dictionary<string, DmxValue> parameters) =>
        new(PathConstraint.Named, PathConstraint.Named, parameters);

    /// <summary>A straight path from control point 0 to 1, and the unpack defaults otherwise.</summary>
    private static Dictionary<string, DmxValue> Path(float midPoint) =>
        new(StringComparer.Ordinal)
        {
            ["start control point number"] = new DmxValue(DmxAttributeType.Whole, Number: 0d),
            ["end control point number"] = new DmxValue(DmxAttributeType.Whole, Number: 1d),
            ["mid point position"] = new DmxValue(DmxAttributeType.Real, Number: midPoint),
        };
}
