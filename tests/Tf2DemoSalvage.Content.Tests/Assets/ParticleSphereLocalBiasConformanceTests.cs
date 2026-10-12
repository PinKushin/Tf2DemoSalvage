using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>`Position Within Sphere Random` with `bias in local system` and `distance_bias_absolute_value` (B529).</summary>
/// <remarks>
/// **`C_INIT_CreateWithinSphere::InitNewParticlesScalar` and `InitParams`, read in `builtin_initializers.obj`:**
///
/// <code>
/// // InitParams
/// m_bDistanceBias    = distance_bias != ( 1, 1, 1 )
/// m_bDistanceBiasAbs = distance_bias_absolute_value != ( 0, 0, 0 )
///
/// // per particle
/// randpos = RandomVectorInUnitSphere;  if m_bDistanceBiasAbs: each axis whose absolute-value entry is not 0 → fabs
/// randpos *= distance_bias;  dir = normalize( randpos );  randpos = dir * lerp( min, max, length )
/// if ( !m_bDistanceBias || !m_bLocalCoords )  pos = randpos + GetControlPointAtTime( cp, creation time )
/// else                                        pos = VectorTransform( randpos, GetControlPointTransformAtTime( cp, … ) )
/// prev = pos − ( dir * speed + local speed along forward, right, up ) * dt     // dir is the UNTURNED one
/// </code>
///
/// The transform's columns are forward, LEFT, up (B415). **The outward speed keeps the world-frame direction even
/// when the position was turned** — the engine's own, reproduced.
/// </remarks>
public sealed class ParticleSphereLocalBiasConformanceTests
{
    private const float Step = 1f / 66f;

    private static readonly Vector3 At = new(500f, 600f, 700f);

    /// <summary>Forward is +Y, right is +X, up is +Z.</summary>
    private static readonly ParticleControlPoint FacingPlusY = new(At, Vector3.UnitY, Vector3.UnitX, Vector3.UnitZ);

    private static readonly ParticleControlPoint Unturned = new(At, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

    [Test]
    public void Spawn_ABiasInTheLocalSystem_LaysTheSphereInThePointsOwnAxes()
    {
        // The medigun trail's bias: nothing along local Y. Local X is the point's forward, world +Y here.
        (ParticleStore store, int count) = Spawned(Sphere(new Vector4(1f, 0f, 1f, 0f), local: true), FacingPlusY);
        float widest = 0f;

        for (int index = 0; index < count; index++)
        {
            Vector3 offset = store.PositionOf(index) - At;

            offset.X.ShouldBe(0f, 1e-3f, "local Y is the point's left, world −X here, and the bias removes it");
            widest = MathF.Max(widest, MathF.Abs(offset.Y));
        }

        widest.ShouldBeGreaterThan(5f, "local X is the point's forward, world +Y");
    }

    [Test]
    public void Spawn_ABiasInTheLocalSystem_StillLaunchesAlongTheUnturnedDirection()
    {
        (ParticleStore store, int count) = Spawned(Sphere(new Vector4(1f, 0f, 1f, 0f), local: true, speed: 100d), FacingPlusY);
        float widest = 0f;

        for (int index = 0; index < count; index++)
        {
            Vector3 velocity = (store.PositionOf(index) - store.PreviousOf(index)) / Step;

            velocity.Y.ShouldBe(0f, 1e-2f, "the biased direction has no world Y, and the speed is along it unturned");
            widest = MathF.Max(widest, MathF.Abs(velocity.X));
        }

        widest.ShouldBeGreaterThan(50f);
    }

    [Test]
    public void Spawn_ALocalSystemWithNoBias_IsNotTurned()
    {
        ParticleFunction sphere = Sphere(new Vector4(1f, 1f, 1f, 0f), local: true);
        (ParticleStore turned, int count) = Spawned(sphere, FacingPlusY);
        (ParticleStore plain, _) = Spawned(sphere, Unturned);

        for (int index = 0; index < count; index++)
        {
            Vector3.Distance(turned.PositionOf(index), plain.PositionOf(index)).ShouldBeLessThan(1e-3f);
        }
    }

    [Test]
    public void Spawn_AnAbsoluteValueAxis_KeepsThatAxisPositive()
    {
        ParticleFunction sphere = new(
            "Position Within Sphere Random",
            "place",
            new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["distance_min"] = new(DmxAttributeType.Real, 10d),
                ["distance_max"] = new(DmxAttributeType.Real, 10d),
                ["distance_bias_absolute_value"] = new(DmxAttributeType.Vector3, Vector: new Vector4(0f, 0f, 1f, 0f)),
            });

        (ParticleStore store, int count) = Spawned(sphere, Unturned);
        int below = 0;
        int left = 0;

        for (int index = 0; index < count; index++)
        {
            Vector3 offset = store.PositionOf(index) - At;

            below += offset.Z < 0f ? 1 : 0;
            left += offset.X < 0f ? 1 : 0;
        }

        below.ShouldBe(0);
        left.ShouldBeGreaterThan(8, "the control: an axis not named still goes both ways");
    }

    /// <remarks>`AngleVectors` at yaw 90: forward +Y, right +X (mathlib.cpp) — the basis <see cref="FacingPlusY"/> spells out.</remarks>
    [Test]
    public void Facing_AYawOfNinety_IsForwardPlusYAndRightPlusX()
    {
        ParticleControlPoint faced = ParticleControlPoint.Facing(At, 90f);

        Vector3.Distance(faced.Forward, Vector3.UnitY).ShouldBeLessThan(1e-6f);
        Vector3.Distance(faced.Right, Vector3.UnitX).ShouldBeLessThan(1e-6f);
        (faced.At, faced.Up).ShouldBe((At, Vector3.UnitZ));
    }

    private static ParticleFunction Sphere(Vector4 bias, bool local, double speed = 0d) =>
        new(
            "Position Within Sphere Random",
            "place",
            new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["distance_min"] = new(DmxAttributeType.Real, 10d),
                ["distance_max"] = new(DmxAttributeType.Real, 10d),
                ["distance_bias"] = new(DmxAttributeType.Vector3, Vector: bias),
                ["bias in local system"] = new(DmxAttributeType.Boolean, local ? 1d : 0d),
                ["speed_min"] = new(DmxAttributeType.Real, speed),
                ["speed_max"] = new(DmxAttributeType.Real, speed),
            });

    private static (ParticleStore Store, int Count) Spawned(ParticleFunction sphere, ParticleControlPoint point)
    {
        ParticleSystem system = new(
            Name: "test_sphere",
            Emitters: [],
            Initializers: [sphere],
            Operators: [],
            Renderers: [],
            Children: [],
            Parameters: new Dictionary<string, DmxValue>(StringComparer.Ordinal));

        ParticleStore store = new();

        for (int one = 0; one < 32; one++)
        {
            ParticleSystems.Spawn(system, store, point, lives: 1f, seconds: Step);
        }

        return (store, 32);
    }
}
