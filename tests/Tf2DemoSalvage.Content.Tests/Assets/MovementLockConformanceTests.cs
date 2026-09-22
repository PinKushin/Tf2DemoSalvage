using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>`C_OP_PositionLock::Operate` — <c>Movement Lock to Control Point</c> — read out of `particles.lib` (B415).</summary>
public sealed class MovementLockConformanceTests
{
    private const float Step = 0.1f;

    [Test]
    public void Operate_FirstCall_StoresThePointAndMovesNothing()
    {
        // The context starts at the origin, and `if ( prev == vec3_origin ) prev = cp` — so the first delta is zero.
        ParticleStore particles = Aged(new Vector3(5f, 0f, 0f));
        MovementLock lockTo = new();

        lockTo.Operate(particles, NoFade(), Step, At(new Vector3(10f, 0f, 0f)));

        particles.PositionOf(0).ShouldBe(new Vector3(5f, 0f, 0f));
    }

    [Test]
    public void Operate_ThePointMoves_AnOldParticleAndItsPreviousMoveWithIt()
    {
        ParticleStore particles = Aged(new Vector3(5f, 0f, 0f));
        MovementLock lockTo = new();

        lockTo.Operate(particles, NoFade(), Step, At(new Vector3(10f, 0f, 0f)));
        lockTo.Operate(particles, NoFade(), Step, At(new Vector3(15f, 0f, 0f)));

        particles.PositionOf(0).ShouldBe(new Vector3(10f, 0f, 0f));
        particles.PreviousOf(0).ShouldBe(new Vector3(10f, 0f, 0f));
    }

    [Test]
    public void Operate_AParticleBornPartWayThroughTheStep_MovesByItsShare()
    {
        // `min( now − born, dt ) / dt`: born half a step ago, it moves half the delta.
        ParticleStore particles = new();

        particles.Tick(1f);
        particles.Add(Vector3.Zero, lives: 10f);
        particles.Tick(Step / 2f);

        MovementLock lockTo = new();

        lockTo.Operate(particles, NoFade(), Step, At(new Vector3(10f, 0f, 0f)));
        lockTo.Operate(particles, NoFade(), Step, At(new Vector3(20f, 0f, 0f)));

        particles.PositionOf(0).X.ShouldBe(5f, 1e-4d);
    }

    [TestCase(0.1f, 5f)]
    [TestCase(0.35f, 2.5f)]
    [TestCase(0.9f, 0f)]
    public void Operate_AFadeWindow_ReleasesTheParticleAcrossIt(float lifeFraction, float moved)
    {
        // `fade = 1 − clamp( ( life − start ) / ( end − start ) )` with start 0.2 and end 0.5: full before, half at
        // 0.35, none after.
        ParticleStore particles = new();

        particles.Add(Vector3.Zero, lives: 10f);
        particles.Tick(lifeFraction * 10f);

        MovementLock lockTo = new();
        Dictionary<string, DmxValue> window = Window(0.2f, 0.5f);

        lockTo.Operate(particles, Function(window), Step, At(new Vector3(10f, 0f, 0f)));
        lockTo.Operate(particles, Function(window), Step, At(new Vector3(15f, 0f, 0f)));

        particles.PositionOf(0).X.ShouldBe(moved, 1e-4d);
    }

    [Test]
    public void Operate_ADistanceFadeRange_BiasesTheShareByDistance()
    {
        // 50 units from a range of 100 is d = 0.5, and `Bias( d, 0.2 ) = d / ( ( 1 − d ) · 3 + 1 )` = 0.2: the particle
        // keeps 0.8 of a 10-unit move.
        ParticleStore particles = Aged(new Vector3(100f, 50f, 0f));
        MovementLock lockTo = new();
        Dictionary<string, DmxValue> ranged = new(NoFadeParameters(), StringComparer.Ordinal)
        {
            ["distance fade range"] = new DmxValue(DmxAttributeType.Real, Number: 100d),
        };

        lockTo.Operate(particles, Function(ranged), Step, At(new Vector3(100f, 0f, 0f)));
        lockTo.Operate(particles, Function(ranged), Step, At(new Vector3(110f, 0f, 0f)));

        particles.PositionOf(0).X.ShouldBe(108f, 1e-3d);
    }

    [Test]
    public void Operate_LockRotation_CarriesTheParticleRoundWithThePoint()
    {
        // `cur · prev⁻¹` applied to the particle: a point turning 90° about Z takes a particle 10 units ahead of it to
        // 10 units to its left.
        ParticleStore particles = Aged(new Vector3(110f, 0f, 0f));
        MovementLock lockTo = new();
        Dictionary<string, DmxValue> rotating = new(NoFadeParameters(), StringComparer.Ordinal)
        {
            ["lock rotation"] = new DmxValue(DmxAttributeType.Boolean, Number: 1d),
        };

        lockTo.Operate(particles, Function(rotating), Step, At(new Vector3(100f, 0f, 0f)));
        lockTo.Operate(
            particles,
            Function(rotating),
            Step,
            new ParticleControlPoint(new Vector3(100f, 0f, 0f), Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ));

        particles.PositionOf(0).X.ShouldBe(100f, 1e-4d);
        particles.PositionOf(0).Y.ShouldBe(10f, 1e-4d);
    }

    private static ParticleStore Aged(Vector3 at)
    {
        ParticleStore particles = new();

        particles.Add(at, lives: 10f);
        particles.Tick(1f);

        return particles;
    }

    private static ParticleControlPoint At(Vector3 at) => new(at, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

    private static ParticleFunction Function(IReadOnlyDictionary<string, DmxValue> parameters) =>
        new(MovementLock.Named, MovementLock.Named, parameters);

    private static ParticleFunction NoFade() => Function(NoFadeParameters());

    /// <summary>`start_fadeout_min` at 1 or more takes the branch with no fade window.</summary>
    private static Dictionary<string, DmxValue> NoFadeParameters() => Window(1f, 1f);

    private static Dictionary<string, DmxValue> Window(float start, float end) =>
        new(StringComparer.Ordinal)
        {
            ["start_fadeout_min"] = new DmxValue(DmxAttributeType.Real, Number: start),
            ["start_fadeout_max"] = new DmxValue(DmxAttributeType.Real, Number: start),
            ["end_fadeout_min"] = new DmxValue(DmxAttributeType.Real, Number: end),
            ["end_fadeout_max"] = new DmxValue(DmxAttributeType.Real, Number: end),
        };
}
