using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A particle's `CREATION_TIME`, and the control point an initializer reads at it (B470).
/// </summary>
/// <remarks>
/// Read in the disassembly of the SDK's `particles.lib` (`particles.obj`, `builtin_particle_emitters.obj`,
/// `builtin_initializers.obj`):
///
/// <code>
/// C_OP_ContinuousEmitter::Emit     start = curtime − dt, end = curtime; a non-zero duration clamps both to the window
///                                  total += ( end − start ) · rate;  n = floor( total ) − emitted
///                                  CREATION_TIME of the k-th = min( start + k · ( end − start ) / n, end )
/// C_OP_InstantaneousEmitter::Emit  CREATION_TIME = emission_start_time, once curtime has reached it
/// GetControlPointAtTime( cp, t )   dt = m_flDt;  dt == 0 → m_Position, else
///                                  lerp( m_PrevPosition, m_Position, max( 0, ( dt − ( curtime − t ) ) / dt ) )
/// Simulate( dt )                   first frame: m_PrevPosition = m_Position;  dt below 2^−74 simulates nothing;
///                                  after the step every m_PrevPosition = m_Position (UpdatePrevControlPoints)
/// C_INIT_CreateWithinSphere        places at GetControlPointAtTime( cp, CREATION_TIME )
/// </code>
///
/// Every case steps a quarter of a second at 8 particles a second, so two are born a step, an eighth of a second apart,
/// and every number is exact in binary.
/// </remarks>
[TestFixture]
public sealed class ParticleCreationTimeConformanceTests
{
    private const float Quarter = 0.25f;

    [Test]
    public void Step_AContinuousEmitterOnAMovingPoint_BornAlongItsPathAtTheirCreationTimes()
    {
        ParticleEffect effect = new(Continuous());

        effect.Step(At(-8f), Quarter);
        effect.Step(At(8f), Quarter);

        Xs(effect).ShouldBe([-8f, -8f, 0f, 8f], "the first step reads the point it was given; the second lerps from it");
        Born(effect).ShouldBe([0.125f, 0.25f, 0.375f, 0.5f]);
    }

    [Test]
    public void Step_ABurstThatStartsMidStep_IsBornAtItsStartTimeAndItsPoint()
    {
        ParticleEffect effect = new(Instantaneous(startTime: 0.375d));

        effect.Step(At(0f), Quarter);
        effect.Step(At(8f), Quarter);

        (Xs(effect).Single(), Born(effect).Single()).ShouldBe((4f, 0.375f));
    }

    [Test]
    public void Step_AnEmitterWhoseDurationEndsMidStep_EmitsUpToItsEnd()
    {
        ParticleEffect effect = new(Continuous(duration: 0.375d));

        effect.Step(At(0f), Quarter);
        effect.Step(At(0f), Quarter);
        effect.Step(At(0f), Quarter);

        Born(effect).ShouldBe([0.125f, 0.25f, 0.375f]);
    }

    [Test]
    public void Step_AnInitializerOnAnotherControlPoint_ReadsItAtTheCreationTime()
    {
        ParticleEffect effect = new(Continuous(sphereOn: 1));

        effect.SetControlPoint(1, At(0f));
        effect.Step(At(100f), Quarter);
        effect.SetControlPoint(1, At(8f));
        effect.Step(At(100f), Quarter);

        Xs(effect).ShouldBe([0f, 0f, 4f, 8f]);
    }

    [Test]
    public void Step_ZeroSeconds_SimulatesNothing()
    {
        ParticleEffect effect = new(Instantaneous(startTime: 0d));

        effect.Step(At(0f), seconds: 0f);

        effect.Particles.Count.ShouldBe(0);

        effect.Step(At(0f), Quarter);

        effect.Particles.Count.ShouldBe(1, "the control: a real step emits it");
    }

    [Test]
    public void Step_ZeroSeconds_LeavesThePreviousPointAlone()
    {
        ParticleEffect effect = new(Continuous());

        effect.Step(At(0f), Quarter);
        effect.Step(At(100f), seconds: 0f);
        effect.Step(At(8f), Quarter);

        Xs(effect).ShouldBe([0f, 0f, 4f, 8f], "the third step lerps from the first's point, not the paused one's");
    }

    [Test]
    public void Step_ThreeSteps_EachLerpsFromThePointTheLastStepEndedAt()
    {
        ParticleEffect effect = new(Continuous());

        effect.Step(At(0f), Quarter);
        effect.Step(At(8f), Quarter);
        effect.Step(At(16f), Quarter);

        Xs(effect).ShouldBe([0f, 0f, 4f, 8f, 12f, 16f]);
    }

    /// <remarks>
    /// A burst held over by its per-frame cap is still dated to its start time, so the second step's particle is a whole
    /// step older than the step's start: `f` is −1 and clamps to 0 — the previous point, not one extrapolated behind it.
    /// </remarks>
    [Test]
    public void Step_ABurstHeldOverByItsPerFrameCap_IsPlacedAtThePreviousPoint()
    {
        ParticleSystem burst = Instantaneous(startTime: 0d, count: 2, perFrame: 1);
        ParticleEffect effect = new(burst);

        effect.Step(At(0f), Quarter);
        effect.Step(At(8f), Quarter);

        Xs(effect).ShouldBe([0f, 0f]);
        Born(effect).ShouldBe([0f, 0f]);
    }

    /// <remarks>With a duration, the window starts at the start time: an eighth of a second at 8 a second is one particle.</remarks>
    [Test]
    public void Step_AWindowedEmitterStartingMidStep_EmitsFromItsStartTime()
    {
        ParticleEffect effect = new(Continuous(duration: 1d, startTime: 0.125d));

        effect.Step(At(0f), Quarter);

        Born(effect).ShouldBe([0.25f]);
    }

    /// <remarks>Without a duration the window is the whole step, even the step the start time falls in.</remarks>
    [Test]
    public void Step_AnEndlessEmitterStartingMidStep_EmitsForTheWholeStep()
    {
        ParticleEffect effect = new(Continuous(startTime: 0.125d));

        effect.Step(At(0f), Quarter);

        Born(effect).ShouldBe([0.125f, 0.25f]);
    }

    /// <remarks>`n = min( n, room )` comes BEFORE the step is divided, so the one particle that fits is born at the end.</remarks>
    [Test]
    public void Step_AtItsCap_SpreadsOnlyWhatFitsAcrossTheStep()
    {
        ParticleEffect effect = new(Continuous(maxParticles: 1));

        effect.Step(At(0f), Quarter);

        Born(effect).ShouldBe([0.25f]);
    }

    /// <remarks>
    /// `if ( end &lt;= t ) t = end`: three births in a tenth of a second accumulate a step of a thirtieth, and the clamp is
    /// what puts the last exactly on the step's end.
    /// </remarks>
    [Test]
    public void Step_TheLastBirthOfAStep_IsTheStepsEndExactly()
    {
        ParticleEffect effect = new(Continuous(rate: 30d));

        effect.Step(At(0f), 0.1f);

        Born(effect)[^1].ShouldBe(0.1f);
    }

    private static ParticleControlPoint At(float x) => ParticleControlPoint.Unoriented(new Vector3(x, 0f, 0f));

    private static float[] Xs(ParticleEffect effect) =>
        [.. Enumerable.Range(0, effect.Particles.Count).Select(index => effect.Particles.PositionOf(index).X)];

    private static float[] Born(ParticleEffect effect) =>
        [.. Enumerable.Range(0, effect.Particles.Count).Select(index => effect.Particles.Born[index])];

    /// <summary>
    /// `maximum time step` 1, so a quarter-second call is one sub-step and each case reads one emission. At the default
    /// 0.1 the call is cut in three (B492), which `ParticleSimulateConformanceTests` covers.
    /// </summary>
    private static Dictionary<string, DmxValue> OneSubStep() =>
        new(StringComparer.Ordinal) { ["maximum time step"] = new DmxValue(DmxAttributeType.Real, 1d) };

    /// <summary>Ten seconds of life, so nothing is reaped while a case runs.</summary>
    private static ParticleFunction TenSeconds() =>
        new("Lifetime Random", "life", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
        {
            ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 10d),
            ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 10d),
        });

    /// <summary>
    /// <paramref name="rate"/> a second from <paramref name="startTime"/>, for <paramref name="duration"/> seconds (0 is
    /// forever), optionally placed on a control point and capped.
    /// </summary>
    private static ParticleSystem Continuous(
        double duration = 0d, int? sphereOn = null, double startTime = 0d, double rate = 8d, int? maxParticles = null)
    {
        List<ParticleFunction> initializers = [TenSeconds()];

        if (sphereOn is { } point)
        {
            initializers.Add(new ParticleFunction("Position Within Sphere Random", "place",
                new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["control_point_number"] = new DmxValue(DmxAttributeType.Whole, point),
                }));
        }

        Dictionary<string, DmxValue> definition = OneSubStep();

        if (maxParticles is { } cap)
        {
            definition["max_particles"] = new DmxValue(DmxAttributeType.Whole, cap);
        }

        return new ParticleSystem(
            "steady",
            [
                new ParticleFunction("emit_continuously", "emit", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["emission_rate"] = new DmxValue(DmxAttributeType.Real, rate),
                    ["emission_duration"] = new DmxValue(DmxAttributeType.Real, duration),
                    ["emission_start_time"] = new DmxValue(DmxAttributeType.Real, startTime),
                }),
            ],
            initializers,
            [],
            [],
            [],
            definition);
    }

    /// <summary><paramref name="count"/> particles at <paramref name="startTime"/>, at most <paramref name="perFrame"/> a step.</summary>
    private static ParticleSystem Instantaneous(double startTime, int count = 1, int perFrame = 100) =>
        new(
            "burst",
            [
                new ParticleFunction("emit_instantaneously", "emit", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["num_to_emit"] = new DmxValue(DmxAttributeType.Whole, count),
                    ["emission_start_time"] = new DmxValue(DmxAttributeType.Real, startTime),
                    ["maximum emission per frame"] = new DmxValue(DmxAttributeType.Whole, perFrame),
                }),
            ],
            [TenSeconds()],
            [],
            [],
            [],
            OneSubStep());
}
