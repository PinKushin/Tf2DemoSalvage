using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// <c>Position on Model Random</c> and <c>Movement Lock to Bone</c>: particles born on, and carried by, the hitboxes of
/// the entity a control point follows (B538).
/// </summary>
/// <remarks>
/// <code>
/// // C_INIT_CreateOnModel::InitNewParticlesScalar, builtin_initializers.obj 0xeeae0
/// UpdateHitBoxInfo( cp );
/// g_pParticleSystemMgr->Query()->GetRandomPointsOnControllingObjectHitBox(
///     pParticles, cp, n, m_flHitBoxScale, m_nForceInModel, pts, m_vecDirectionBias, relative, indices );
/// XYZ = pts[i];  PREV_XYZ = pts[i];  HITBOX_RELATIVE_XYZ = relative[i];  HITBOX_INDEX = indices[i];
///
/// // its unpack table, 0xfd420: "control_point_number" int +0x38 "0", "force to be inside model" int +0x3c "0",
/// // "hitbox scale" INT +0x40 "1.0", "direction bias" vector +0x44 "0 0 0" — and the member is passed on as a float
///
/// // CParticleSystemQuery::GetRandomPointsOnControllingObjectHitBox, particlesystemquery.cpp:187-389
/// flRandMax = flBBoxScale;  flRandMin = 1.0 - flBBoxScale;  vecBasePos = GetControlPointAtTime( cp, m_flCurTime );
/// nNumIters = nNumTrysToGetAPointInsideTheModel;  if ( !vecDirectionalBias.IsZero( 0.0001 ) ) nNumIters = MAX( nNumIters, 5 );
/// for each point:  nTryCnt = nNumIters;  best = -1e20;
///   do {
///     box = RandomInt( 0, numhitboxes - 1 );  u, v, w = RandomFloat( flRandMin, flRandMax ) x 3;
///     world = VectorTransform( Lerp( uvw, bbmin * scale, bbmax * scale ), hitboxbones[ box->bone ] );
///     goodness = RandomFloat( 0, 72 ) + DotProduct( world - vecBasePos, vecDirectionalBias );
///     if ( goodness > best ) keep u, v, w, world, box
///   } while ( nTryCnt-- );
/// no entity, model or set:  every point is m_ControlPoints[ cp ].m_Position, index 0, relative ( 0, 0, 0 )
///
/// // C_OP_LockToBone::Operate, builtin_particle_ops.obj 0x115d90; "lifetime fade start" +0x3c, "lifetime fade end" +0x40, both "0"
/// UpdateHitBoxInfo( cp );  if ( !CurAndPrevValid() ) return;          // numhitboxes and prev count == count
/// end = fadeEnd > 0 ? fadeEnd : huge;  rate = fadeStart &lt; fadeEnd ? 1 / ( fadeEnd - fadeStart ) : 0;
/// for each particle:  age = m_flCurTime - CREATION_TIME;
///   if ( age &lt; end &amp;&amp; 0 &lt;= HITBOX_INDEX &lt; count )
///     delta = cur[ box ].Transform( Lerp( relative, cur.mins, cur.maxs ) ) - prev[ box ].Transform( Lerp( relative, prev.mins, prev.maxs ) );
///     if ( fadeStart &lt; age ) delta *= ( 1 - ( age - fadeStart ) * rate ) * flStrength;
///     XYZ += delta;  PREV_XYZ += delta;
/// </code>
/// **The scale is an int read as a float**, so a declared 1 is 1.4e-45 and the range is ( 1, ~0 ): each coordinate is
/// one minus its draw. Still uniform across the box; the mirror is kept so a draw lands where the engine's does.
/// </remarks>
public sealed class ModelHitboxConformanceTests
{
    private const int Seed = 77;

    private static readonly ParticleControlPoint Base = ParticleControlPoint.Unoriented(new Vector3(100f, 200f, 300f));

    private static ModelHitbox Box(Vector3 origin) => ModelHitbox.At(Vector3.Zero, new Vector3(10f, 20f, 40f), origin);

    private static ParticleFunction Initializer(Vector4 bias = default, int scale = 1) =>
        new(
            PositionOnModel.Named,
            PositionOnModel.Named,
            new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["direction bias"] = new DmxValue(DmxAttributeType.Vector3, Vector: bias),
                ["hitbox scale"] = new DmxValue(DmxAttributeType.Whole, Number: scale),
            });

    private static ParticleFunction Lock(float start, float end) =>
        new(
            LockToBone.Named,
            LockToBone.Named,
            new Dictionary<string, DmxValue>(StringComparer.Ordinal)
            {
                ["lifetime fade start"] = new DmxValue(DmxAttributeType.Real, Number: start),
                ["lifetime fade end"] = new DmxValue(DmxAttributeType.Real, Number: end),
            });

    private static ParticleStore One()
    {
        ParticleStore particles = new() { Seed = Seed };

        particles.Add(new Vector3(1f, 2f, 3f), lives: 2f);

        return particles;
    }

    [Test]
    public void Place_NoModelOnThePoint_IsTheControlPointWithNoDraws()
    {
        ParticleStore particles = One();

        PositionOnModel.Place(Initializer(new Vector4(0f, 0f, 1f, 0f)), particles, 0, Base, []);

        particles.PositionOf(0).ShouldBe(Base.At);
        particles.PreviousOf(0).ShouldBe(Base.At);
        particles.HitboxIndex[0].ShouldBe(0);
        particles.HitboxRelative[0].ShouldBe(Vector3.Zero);
        particles.Queries.ShouldBe(0, "bSucesss stays false before anything is drawn");
    }

    [Test]
    public void Place_OneBoxAndNoBias_TakesOneTryOfFiveDrawsAndMirrorsEachCoordinate()
    {
        ParticleStore particles = One();
        Vector3 origin = new(500f, 600f, 700f);

        PositionOnModel.Place(Initializer(), particles, 0, Base, [Box(origin)]);

        // Query 0 is the box, 1..3 the coordinates, 4 the goodness.
        Vector3 relative = new(
            1f - ParticleRandom.Sample(Seed, 1, 0),
            1f - ParticleRandom.Sample(Seed, 2, 0),
            1f - ParticleRandom.Sample(Seed, 3, 0));

        particles.Queries.ShouldBe(5);
        particles.HitboxIndex[0].ShouldBe(0);
        Near(particles.HitboxRelative[0], relative);
        Near(particles.PositionOf(0), origin + (relative * new Vector3(10f, 20f, 40f)));
        particles.PreviousOf(0).ShouldBe(particles.PositionOf(0), "launched with no speed");
    }

    [Test]
    public void Place_ADirectionBias_TakesSixTriesAndKeepsTheOneFurthestAlongIt()
    {
        // Two boxes a thousand units apart in height: the 72 of noise cannot outweigh that, so the high one wins
        // whenever any of the six tries draws it — and the control is that the tries here drew both.
        ModelHitbox[] boxes = [Box(new Vector3(0f, 0f, 0f)), Box(new Vector3(0f, 0f, 1000f))];
        ParticleStore particles = One();

        PositionOnModel.Place(Initializer(new Vector4(0f, 0f, 1f, 0f)), particles, 0, Base, boxes);

        HashSet<int> tried = [];

        for (int attempt = 0; attempt < 6; attempt++)
        {
            tried.Add(ParticleRandom.Whole(Seed, attempt * 5, 0, 0, 1));
        }

        tried.Count.ShouldBe(2, "the seed must try both boxes for the choice to mean anything");
        particles.Queries.ShouldBe(30, "do { } while ( nTryCnt-- ) from 5 is six tries of five draws");
        particles.HitboxIndex[0].ShouldBe(1);
        particles.PositionOf(0).Z.ShouldBeGreaterThanOrEqualTo(1000f);
    }

    [Test]
    public void Operate_TheFirstFrameWithNoPreviousBoxes_MovesNothing()
    {
        ParticleStore particles = Placed(age: 0.5f);
        Vector3 before = particles.PositionOf(0);

        LockToBone.Operate(particles, Lock(1f, 2f), [Box(new Vector3(5f, 0f, 0f))], []);

        particles.PositionOf(0).ShouldBe(before);
    }

    [TestCase(0.5f, 5f)]
    [TestCase(1.5f, 2.5f)]
    [TestCase(2.5f, 0f)]
    public void Operate_ABoxThatMoved_CarriesAParticleByItsAgeAgainstTheFade(float age, float moved)
    {
        ParticleStore particles = Placed(age);
        Vector3 before = particles.PositionOf(0);
        Vector3 previous = particles.PreviousOf(0);

        LockToBone.Operate(particles, Lock(1f, 2f), [Box(new Vector3(5f, 0f, 0f))], [Box(Vector3.Zero)]);

        Near(particles.PositionOf(0), before + new Vector3(moved, 0f, 0f));
        Near(particles.PreviousOf(0), previous + new Vector3(moved, 0f, 0f));
    }

    [Test]
    public void Operate_NoFadeDeclared_LocksAtAnyAge()
    {
        // Both default to 0: the end becomes "never" and the rate 0, so the factor past the start is 1.
        ParticleStore particles = Placed(age: 30f);
        Vector3 before = particles.PositionOf(0);

        LockToBone.Operate(particles, Lock(0f, 0f), [Box(new Vector3(5f, 0f, 0f))], [Box(Vector3.Zero)]);

        Near(particles.PositionOf(0), before + new Vector3(5f, 0f, 0f));
    }

    [Test]
    public void Operate_ABoxThatTurned_CarriesTheParticleRoundWithItsBone()
    {
        ParticleStore particles = Placed(age: 0.5f);

        particles.HitboxRelative[0] = new Vector3(1f, 0.5f, 0.5f);
        particles.Position[0] = new Vector3(10f, 10f, 20f);
        particles.Previous[0] = particles.Position[0];

        // A quarter turn about Z: the bone's x axis now points along world y.
        ModelHitbox turned = new(
            Vector3.Zero,
            new Vector3(10f, 20f, 40f),
            new Vector4(0f, -1f, 0f, 0f),
            new Vector4(1f, 0f, 0f, 0f),
            new Vector4(0f, 0f, 1f, 0f));

        LockToBone.Operate(particles, Lock(1f, 2f), [turned], [Box(Vector3.Zero)]);

        Near(particles.PositionOf(0), new Vector3(-10f, 10f, 20f));
    }

    [TestCase(-1)]
    [TestCase(1)]
    public void Operate_AnIndexOutsideTheSet_IsLeftAlone(int index)
    {
        ParticleStore particles = Placed(age: 0.5f);
        Vector3 before = particles.PositionOf(0);

        particles.HitboxIndex[0] = index;

        LockToBone.Operate(particles, Lock(1f, 2f), [Box(new Vector3(5f, 0f, 0f))], [Box(Vector3.Zero)]);

        particles.PositionOf(0).ShouldBe(before);
    }

    [Test]
    public void Operate_ASetThatChangedSize_MovesNothing()
    {
        ParticleStore particles = Placed(age: 0.5f);
        Vector3 before = particles.PositionOf(0);

        LockToBone.Operate(
            particles, Lock(1f, 2f), [Box(new Vector3(5f, 0f, 0f))], [Box(Vector3.Zero), Box(Vector3.Zero)]);

        particles.PositionOf(0).ShouldBe(before);
    }

    [Test]
    public void Step_AnEffectOnAMovingModel_BearsItsParticlesOnTheBoxAndCarriesThem()
    {
        // The wiring: the effect hands each function the boxes it was given for that control point, this step's and
        // the last one's.
        ParticleSystem system = new(
            "on_model",
            [
                new ParticleFunction(
                    "emit_instantaneously",
                    "emit_instantaneously",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["num_to_emit"] = new DmxValue(DmxAttributeType.Whole, Number: 8d),
                    }),
            ],
            [Initializer()],
            [Lock(0f, 0f)],
            [],
            [],
            new Dictionary<string, DmxValue>(StringComparer.Ordinal));

        ParticleEffect effect = new(system, seed: Seed);
        Vector3 size = new(10f, 20f, 40f);

        effect.SetHitboxes(0, [Box(new Vector3(1000f, 0f, 0f))]);
        effect.Step(Base, 0.015f);

        effect.Particles.Count.ShouldBe(8);

        for (int index = 0; index < 8; index++)
        {
            Vector3 at = effect.Particles.PositionOf(index) - new Vector3(1000f, 0f, 0f);

            at.X.ShouldBeInRange(0f, size.X);
            at.Y.ShouldBeInRange(0f, size.Y);
            at.Z.ShouldBeInRange(0f, size.Z);
        }

        Vector3 first = effect.Particles.PositionOf(0);

        effect.SetHitboxes(0, [Box(new Vector3(1007f, 0f, 0f))]);
        effect.Step(Base, 0.015f);

        Near(effect.Particles.PositionOf(0), first + new Vector3(7f, 0f, 0f));
    }

    private static ParticleStore Placed(float age)
    {
        ParticleStore particles = One();

        particles.HitboxRelative[0] = new Vector3(0.5f, 0.5f, 0.5f);
        particles.Position[0] = new Vector3(5f, 10f, 20f);
        particles.Previous[0] = new Vector3(4f, 10f, 20f);
        particles.Tick(age);

        return particles;
    }

    private static void Near(Vector3 actual, Vector3 expected)
    {
        actual.X.ShouldBe(expected.X, 1e-3d);
        actual.Y.ShouldBe(expected.Y, 1e-3d);
        actual.Z.ShouldBe(expected.Z, 1e-3d);
    }
}
