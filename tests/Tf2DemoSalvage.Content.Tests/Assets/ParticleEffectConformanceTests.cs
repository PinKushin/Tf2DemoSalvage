using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// One running particle system: emit, operate, reap (B373).
/// </summary>
[TestFixture]
public sealed class ParticleEffectConformanceTests
{
    [Test]
    public void Step_AFractionalEmissionRate_CarriesTheRemainderRatherThanLosingIt()
    {
        // **The rate is a fraction of a particle per step and truncating each step loses a third of
        // the trail.** At the shipped `emission_rate 128` on a 66-tick clock that is 1.94 a step:
        // truncating emits 1 and rounding emits 2, and only carrying the remainder gives the
        // declared rate over time.
        //
        // Ten steps at 1.5 a step must produce 15, not 10 and not 20.
        ParticleEffect effect = new(System("emission_rate", 99d));

        for (int step = 0; step < 10; step++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 1f / 66f);
        }

        // 99 per second over 10/66 of a second = 15.
        effect.Particles.Count.ShouldBe(15);
    }

    [Test]
    public void Step_AnEmissionDurationOfZero_MeansForeverAndNotNever()
    {
        // **The one sentinel in this file**, and reading it the other way emits nothing at all.
        // `rockettrail` declares `emission_duration 0` and trails for as long as the rocket flies.
        ParticleEffect effect = new(System("emission_rate", 66d));

        for (int step = 0; step < 30; step++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 1f / 66f);
        }

        effect.Particles.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public void Step_ParticlesPastTheirLifetime_AreGoneRatherThanAccumulating()
    {
        // The reap, and the control is that SOME survive: an effect that removed everything would
        // pass a bare "count stops growing" assertion.
        ParticleEffect effect = new(System("emission_rate", 66d));

        for (int step = 0; step < 200; step++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 1f / 66f);
        }

        // A lifetime of 1 second at 66 a second settles near 66, and certainly not near 200.
        effect.Particles.Count.ShouldBeLessThan(100);
        effect.Particles.Count.ShouldBeGreaterThan(0);
    }

    [Test]
    public void Step_AtMaxParticles_StopsEmittingRatherThanGrowing()
    {
        // `max_particles` is the engine's collection size, so a system at its cap emits nothing.
        Dictionary<string, DmxValue> declared = new(StringComparer.Ordinal)
        {
            ["max_particles"] = new DmxValue(DmxAttributeType.Whole, 5d),
        };

        ParticleEffect effect = new(System("emission_rate", 660d, declared));

        for (int step = 0; step < 20; step++)
        {
            effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 1f / 66f);
        }

        effect.Particles.Count.ShouldBe(5);
    }

    [Test]
    public void Implemented_ASystemNamingAnUnknownOperator_SaysSoRatherThanSkippingSilently()
    {
        // **A missing operator is invisible in the result** - the effect still draws, just wrongly.
        // So the count is reportable, which is what let the probe say "4 of 4" for rockettrail and
        // "2 of 4" before the two it actually uses were written.
        ParticleSystem system = new(
            "trail",
            [],
            [],
            [
                new ParticleFunction("Movement Basic", "move", Empty),
                new ParticleFunction("Some Operator We Have Not Written", "odd", Empty),
            ],
            [],
            [],
            Empty);

        new ParticleEffect(system).Implemented().ShouldBe(1);
    }

    /// <remarks>
    /// **A tracer's end is control point 1, set on the effect before it first steps** (`ParticleEffectCallback`,
    /// `c_particle_system.cpp`), and the engine passes it to every child. A child running the same initializer must
    /// head for the same point, or a tracer with a glow child would split in two.
    /// </remarks>
    [Test]
    public void SetControlPoint_BeforeTheFirstStep_ReachesTheSpawnAndEveryChild()
    {
        ParticleSystem tracer = Tracer("tracer", children: ["glow"]);
        ParticleSystem glow = Tracer("glow", children: []);

        ParticleEffect effect = new(
            tracer, new Dictionary<string, ParticleSystem>(StringComparer.OrdinalIgnoreCase) { ["glow"] = glow });

        effect.SetControlPoint(1, ParticleControlPoint.Unoriented(new Vector3(0f, 500f, 0f)));
        effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 1f / 66f);

        // 500 units at 5000 per second is a tenth of a second. LIFE_DURATION is the whole trip, not what is left.
        effect.Particles.Count.ShouldBe(1);
        effect.Particles.Lifetime[0].ShouldBe(0.1f, 0.0001f);
        effect.Children[0].Particles.Lifetime[0].ShouldBe(0.1f, 0.0001f);
    }

    [Test]
    public void Step_AConstrainedSystem_HoldsItsParticleOnThePathAfterMoving()
    {
        // `C_OP_BasicMovement::Operate` applies the definition's constraints after integrating (B396). A particle born at
        // control point 0 with a band of zero sits on the path point: half-way along a one-second travel, ( 50, 0, 0 ).
        // The burst dates it to its start time, 0 (B470), so two quarter-second steps make it half a second old. Before
        // B470 it was dated to the end of its first step, and this case stepped twice by a half.
        ParticleSystem constrained = new ParticleSystem(
            "beam",
            [
                new ParticleFunction("emit_instantaneously", "emit",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["num_to_emit"] = new DmxValue(DmxAttributeType.Whole, 1d),
                    }),
            ],
            [
                new ParticleFunction("Lifetime Random", "life",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 10d),
                        ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 10d),
                    }),
            ],
            [new ParticleFunction("Movement Basic", "move", Empty)],
            [],
            [],
            Empty)
        {
            Constraints =
            [
                new ParticleFunction(PathConstraint.Named, "path",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["end control point number"] = new DmxValue(DmxAttributeType.Whole, 1d),
                        ["maximum distance"] = new DmxValue(DmxAttributeType.Real, 0d),
                        ["travel time"] = new DmxValue(DmxAttributeType.Real, 1d),
                    }),
            ],
        };

        ParticleEffect effect = new(constrained);

        effect.SetControlPoint(1, ParticleControlPoint.Unoriented(new Vector3(100f, 0f, 0f)));
        effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 0.25f);
        effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 0.25f);

        effect.Particles.PositionOf(0).X.ShouldBe(50f, 1e-3f);
    }

    /// <remarks>
    /// `CParticleCollection::Init( pDef, flDelay, nRandomSeed )`, read in the disassembly of `particles.lib`'s
    /// `particles.obj` (B469): a non-zero seed is kept as `m_nRandomSeed`, and the child loop computes each child's seed
    /// at the top of every pass — `lea eax,[r15+0x81]; test r15d,r15d; cmovz eax,r15d; mov r15d,eax` — so entry k of the
    /// definition's `children` gets the seed plus 129 × k, counting an entry that fails to resolve.
    /// </remarks>
    [Test]
    public void Constructor_ASeed_GivesEachChildTheSeedPlus129PerEntry()
    {
        ParticleEffect effect = new(Named("parent", ["a", "missing", "b"]), Others("a", "b"), sheets: null, seed: 1000);

        (effect.Particles.Seed, effect.Children[0].Particles.Seed, effect.Children[1].Particles.Seed).ShouldBe((1000, 1129, 1387));
    }

    /// <remarks>`cmovz`: a zero seed gives every child zero (B469).</remarks>
    [Test]
    public void Constructor_NoSeed_GivesEveryChildZero()
    {
        ParticleEffect effect = new(Named("parent", ["a", "missing", "b"]), Others("a", "b"));

        (effect.Particles.Seed, effect.Children[0].Particles.Seed, effect.Children[1].Particles.Seed).ShouldBe((0, 0, 0));
    }

    /// <remarks>
    /// **The seed is in every index** (`particles.h:1791`, B469): a seeded collection's first particle draws `Lifetime
    /// Random` from the entry the seed moves it to, which is the seed-0 draw at an offset larger by the seed.
    /// </remarks>
    [Test]
    public void Step_ASeededEffect_DrawsAtTheSeededIndex()
    {
        ParticleEffect effect = new(Burst(lifetimeLeast: 10d, lifetimeMost: 110d), others: null, sheets: null, seed: 500);

        effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 0.25f);

        effect.Particles.LifetimeOf(0).ShouldBe(ParticleRandom.Between(0, 0, ParticleSystems.LifetimeDraw + 500, 10f, 110f));
    }

    /// <remarks>
    /// `C_OP_InstantaneousEmitter::InitializeContextData`, read in `builtin_particle_emitters.obj` (B472): a minimum of 0
    /// or more always draws, `(int)( ( num_to_emit − minimum + 1 ) · r ) + minimum` — with no special case for a minimum
    /// at or above `num_to_emit`. 3 and 5 give `(int)( −r ) + 5`, which is 5 for every draw.
    /// </remarks>
    [Test]
    public void Step_ABurstMinimumAboveItsCount_EmitsTheEnginesDraw()
    {
        ParticleSystem burst = Burst(lifetimeLeast: 10d, lifetimeMost: 10d) with
        {
            Emitters =
            [
                new ParticleFunction("emit_instantaneously", "emit",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["num_to_emit"] = new DmxValue(DmxAttributeType.Whole, 3d),
                        ["num_to_emit_minimum"] = new DmxValue(DmxAttributeType.Whole, 5d),
                    }),
            ],
        };

        ParticleEffect effect = new(burst);

        effect.Step(ParticleControlPoint.Unoriented(Vector3.Zero), seconds: 0.25f);

        effect.Particles.Count.ShouldBe(5);
    }

    /// <remarks>
    /// `SetControlPointOrientation` applies forward, right and up only when `|forward·up|`, `|forward·right|` and
    /// `|right·up|` are each at most 0.1, and otherwise warns and keeps the old orientation (`particles.h:1616-1640`);
    /// `SetControlPoint` writes the position regardless (`:1595-1604`). B471.
    /// </remarks>
    [Test]
    public void SetControlPoint_ForwardAlongUp_MovesThePointAndKeepsItsOrientation() =>
        Refused(Vector3.UnitX, -Vector3.UnitY, new Vector3(0.5f, 0f, 1f));

    [Test]
    public void SetControlPoint_ForwardAlongRight_MovesThePointAndKeepsItsOrientation() =>
        Refused(Vector3.UnitX, new Vector3(0.5f, -1f, 0f), Vector3.UnitZ);

    [Test]
    public void SetControlPoint_RightAlongUp_MovesThePointAndKeepsItsOrientation() =>
        Refused(Vector3.UnitX, -Vector3.UnitY, new Vector3(0f, -0.5f, 1f));

    /// <remarks>`fabs( DotProduct( forward, right ) ) &lt;= 0.1f`: exactly a tenth is accepted, just over is not.</remarks>
    [TestCase(0.1f, true)]
    [TestCase(0.11f, false)]
    public void SetControlPoint_ForwardDotRightNearATenth_IsAcceptedUpToIt(float dot, bool accepted)
    {
        ParticleEffect effect = new(Named("p", []));
        Vector3 right = new(dot, -1f, 0f);

        effect.SetControlPoint(1, Oriented);
        effect.SetControlPoint(1, new ParticleControlPoint(Vector3.Zero, Vector3.UnitX, right, Vector3.UnitZ));

        effect.ControlPoint(1).Right.ShouldBe(accepted ? right : Oriented.Right);
    }

    /// <remarks>The check runs in every child, as the engine's recursion does (`particles.h:1631-1634`).</remarks>
    [Test]
    public void SetControlPoint_AnInvalidBasis_IsRefusedByEveryChild()
    {
        ParticleEffect effect = new(Named("parent", ["a"]), Others("a"));

        effect.SetControlPoint(1, Oriented);
        effect.SetControlPoint(1, new ParticleControlPoint(Moved, Vector3.UnitX, Vector3.UnitX, Vector3.UnitZ));

        effect.Children[0].ControlPoint(1).ShouldBe(Oriented with { At = Moved });
    }

    /// <remarks>Control point 0, which <see cref="ParticleEffect.Step"/> sets, is checked the same way.</remarks>
    [Test]
    public void Step_AnAtWhoseBasisIsNotPerpendicular_KeepsControlPointZerosOrientation()
    {
        ParticleEffect effect = new(Named("p", []));

        effect.Step(Oriented, seconds: 0.25f);
        effect.Step(new ParticleControlPoint(Moved, Vector3.UnitX, Vector3.UnitX, Vector3.UnitZ), seconds: 0.25f);

        effect.ControlPoint(0).ShouldBe(Oriented with { At = Moved });
    }

    /// <summary>Sets control point 1 to <see cref="Oriented"/>, then to <see cref="Moved"/> with the given basis, and expects only the move.</summary>
    private static void Refused(Vector3 forward, Vector3 right, Vector3 up)
    {
        ParticleEffect effect = new(Named("p", []));

        effect.SetControlPoint(1, Oriented);
        effect.SetControlPoint(1, new ParticleControlPoint(Moved, forward, right, up));

        effect.ControlPoint(1).ShouldBe(Oriented with { At = Moved });
    }

    /// <summary>A valid frame — `AngleVectors( 0, 0, 0 )`.</summary>
    private static readonly ParticleControlPoint Oriented = new(Vector3.Zero, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

    /// <summary>Where a refused orientation still moves the point to.</summary>
    private static readonly Vector3 Moved = new(5f, 0f, 0f);

    /// <summary>A system that does nothing, with the named children.</summary>
    private static ParticleSystem Named(string name, IReadOnlyList<string> children) => new(name, [], [], [], [], children, Empty);

    /// <summary>Childless systems by name, for a parent to resolve.</summary>
    private static Dictionary<string, ParticleSystem> Others(params string[] names)
    {
        Dictionary<string, ParticleSystem> others = new(StringComparer.OrdinalIgnoreCase);

        foreach (string name in names)
        {
            others[name] = Named(name, []);
        }

        return others;
    }

    /// <summary>One particle at once, living between <paramref name="lifetimeLeast"/> and <paramref name="lifetimeMost"/> seconds.</summary>
    private static ParticleSystem Burst(double lifetimeLeast, double lifetimeMost) =>
        new(
            "burst",
            [
                new ParticleFunction("emit_instantaneously", "emit",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["num_to_emit"] = new DmxValue(DmxAttributeType.Whole, 1d),
                    }),
            ],
            [
                new ParticleFunction("Lifetime Random", "life",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, lifetimeLeast),
                        ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, lifetimeMost),
                    }),
            ],
            [],
            [],
            [],
            Empty);

    /// <summary>One particle at once, sent at 5000 units a second toward control point 1.</summary>
    private static ParticleSystem Tracer(string name, IReadOnlyList<string> children) =>
        new(
            name,
            [
                new ParticleFunction("emit_instantaneously", "emit",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["num_to_emit"] = new DmxValue(DmxAttributeType.Whole, 1d),
                    }),
            ],
            [
                new ParticleFunction("move particles between 2 control points", "move",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["minimum speed"] = new DmxValue(DmxAttributeType.Real, 5000d),
                        ["maximum speed"] = new DmxValue(DmxAttributeType.Real, 5000d),
                    }),
            ],
            [],
            [],
            children,
            Empty);

    /// <summary>No parameters, so everything takes its default.</summary>
    private static readonly Dictionary<string, DmxValue> Empty = new(StringComparer.Ordinal);

    /// <summary>A system with one continuous emitter and a one-second lifetime.</summary>
    private static ParticleSystem System(
        string named, double value, Dictionary<string, DmxValue>? definition = null) =>
        new(
            "trail",
            [
                new ParticleFunction("emit_continuously", "emit",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        [named] = new DmxValue(DmxAttributeType.Real, value),
                    }),
            ],
            [
                new ParticleFunction("Lifetime Random", "life",
                    new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                    {
                        ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 1d),
                        ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 1d),
                    }),
            ],
            [],
            [],
            [],
            definition ?? Empty);
}
