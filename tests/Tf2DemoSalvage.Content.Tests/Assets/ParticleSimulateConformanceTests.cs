using System;
using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// `CParticleCollection::Simulate`'s step: how one call is cut into the sub-steps the operators see (B492).
/// </summary>
/// <remarks>
/// Read in the disassembly of the SDK's `particles.lib` (`?Simulate@CParticleCollection@@QEAAXM_N@Z`, `particles.obj`). The
/// definition's fields are `m_flMaximumTimeStep` (+0x204, "maximum time step", default "0.1"), `m_flMaximumSimTime`
/// (+0x208, "maximum sim tick rate", default 0), `m_flMinimumSimTime` (+0x20c, "minimum sim tick rate") and
/// `m_nMinimumFrames` (+0x210, "minimum rendered frames"), named by `particles.h:2228-2233` and by the unpack table's strings:
/// <code>
/// step = m_flMaximumTimeStep &gt; 0 ? it : 0.1
/// if ( m_flMaximumSimTime != 0 and m_nSimulatedFrames &lt;= m_nMinimumFrames ):
///     if ( curtime + dt &gt; m_flMaximumSimTime ):  dt = max( m_flMaximumSimTime − curtime, m_flMinimumSimTime )
///     m_nSimulatedFrames++
/// left = min( dt, step · 10 )
/// while ( left &gt; 0 ):  m_flDt = min( left, step );  left −= m_flDt;  m_flCurTime += m_flDt;  emit and operate
/// </code>
/// </remarks>
[TestFixture]
public sealed class ParticleSimulateConformanceTests
{
    [Test]
    public void Step_AQuarterSecondAtTheDefaultMaximumTimeStep_RunsThreeSubSteps()
    {
        ParticleEffect effect = new(Defined());

        effect.Step(ParticleControlPoint.Unset, 0.25f);

        effect.Particles.Steps.ShouldBe(3);
        effect.Particles.LastStep.ShouldBe(0.05f, 1e-6f);
        effect.Particles.Age.ShouldBe(0.25f, 1e-6f);
    }

    [Test]
    public void Step_ADeclaredMaximumTimeStep_CutsAtIt()
    {
        ParticleEffect effect = new(Defined(("maximum time step", 0.25d)));

        effect.Step(ParticleControlPoint.Unset, 0.25f);

        (effect.Particles.Steps, effect.Particles.LastStep).ShouldBe((1, 0.25f));
    }

    /// <remarks>`fVar27 = min( dt, step · 10 )`: a two-second call simulates one second at the default step.</remarks>
    [Test]
    public void Step_LongerThanTenMaximumSteps_SimulatesOnlyTen()
    {
        ParticleEffect effect = new(Defined());

        effect.Step(ParticleControlPoint.Unset, 2f);

        effect.Particles.Steps.ShouldBe(10);
        effect.Particles.Age.ShouldBe(1f, 1e-5f);
    }

    /// <remarks>
    /// With `maximum sim tick rate` 0.3 and `minimum rendered frames` left at 0, only the first call is clamped: it runs
    /// the 0.3 left to the maximum, and the second, its frame count now 1, runs its whole 0.4.
    /// </remarks>
    [Test]
    public void Step_AMaximumSimTime_ClampsOnlyTheFramesTheMinimumAllows()
    {
        ParticleEffect effect = new(Defined(("maximum time step", 1d), ("maximum sim tick rate", 0.3d)));

        effect.Step(ParticleControlPoint.Unset, 0.4f);
        float first = effect.Particles.Age;
        effect.Step(ParticleControlPoint.Unset, 0.4f);

        first.ShouldBe(0.3f, 1e-6f);
        effect.Particles.Age.ShouldBe(0.7f, 1e-6f);
    }

    /// <remarks>`if ( fVar21 &lt;= m_flMinimumSimTime ) fVar21 = m_flMinimumSimTime`: past the maximum, the minimum still runs.</remarks>
    [Test]
    public void Step_PastTheMaximumSimTime_StillRunsTheMinimum()
    {
        ParticleEffect effect = new(Defined(
            ("maximum time step", 1d), ("maximum sim tick rate", 0.3d), ("minimum sim tick rate", 0.05d), ("minimum rendered frames", 1d)));

        effect.Step(ParticleControlPoint.Unset, 0.4f);
        effect.Step(ParticleControlPoint.Unset, 0.4f);

        effect.Particles.Age.ShouldBe(0.35f, 1e-6f);
    }

    /// <remarks>
    /// A child runs its own `Simulate( dt )` with the parent's whole step, so a child declaring a shorter maximum step cuts
    /// the same call more finely. A sixteenth, because it is exact: a declared 0.05 is a float just above a twentieth,
    /// so five of them fall short of a quarter and the engine's `left` loop runs a sixth, tiny one.
    /// </remarks>
    [Test]
    public void Step_AChildWithItsOwnMaximumTimeStep_CutsTheWholeStepItself()
    {
        ParticleSystem child = Defined(("maximum time step", 0.0625d)) with { Name = "child" };
        ParticleSystem parent = Defined(("maximum time step", 1d)) with { Children = ["child"] };

        ParticleEffect effect = new(parent, new Dictionary<string, ParticleSystem>(StringComparer.OrdinalIgnoreCase) { ["child"] = child });

        effect.Step(ParticleControlPoint.Unset, 0.25f);

        (effect.Particles.Steps, effect.Children[0].Particles.Steps).ShouldBe((1, 4));
    }

    /// <remarks>
    /// `m_flPreviousDt` (+0x40) is 0.05 from the constructor and `SimulateFirstFrame`, and `Simulate` sets it to the
    /// call's WHOLE `dt` after the children run — not to a sub-step (B494).
    /// </remarks>
    [Test]
    public void PreviousStep_BeforeAndAfterACall_IsTheFirstFramesTwentiethThenTheWholeCall()
    {
        ParticleEffect effect = new(Defined());
        float before = effect.Particles.PreviousStep;

        effect.Step(ParticleControlPoint.Unset, 0.25f);

        (before, effect.Particles.PreviousStep).ShouldBe((0.05f, 0.25f));
    }

    /// <remarks>
    /// `C_INIT_CreateWithinSphere::InitNewParticlesScalar` writes `PREV_XYZ = XYZ − velocity · m_flPreviousDt`
    /// (`*(param_2 + 0x40)`, `builtin_initializers.obj`, B494). On the first call that is 0.05, whatever the step: ten units
    /// a second down local Z puts the previous position half a unit above.
    /// </remarks>
    [Test]
    public void Step_ASphereSpeedOnTheFirstCall_ScalesByTheTwentiethNotTheStep()
    {
        ParticleEffect effect = new(Launched("Position Within Sphere Random", startTime: 0d));

        effect.Step(Oriented(Vector3.Zero), 1f / 66f);

        (effect.Particles.PreviousOf(0) - effect.Particles.PositionOf(0)).Z.ShouldBe(0.5f, 1e-5f);
    }

    /// <remarks>A particle born in the second call takes the first call's whole step, 0.02, as `m_flPreviousDt`.</remarks>
    [Test]
    public void Step_ASphereSpeedOnALaterCall_ScalesByThePreviousCall()
    {
        ParticleEffect effect = new(Launched("Position Within Sphere Random", startTime: 0.025d));

        effect.Step(Oriented(Vector3.Zero), 0.02f);
        effect.Step(Oriented(Vector3.Zero), 0.01f);

        (effect.Particles.PreviousOf(0) - effect.Particles.PositionOf(0)).Z.ShouldBe(10f * 0.02f, 1e-5f);
    }

    /// <remarks>`C_INIT_MoveBetweenPoints` scales its `PREV_XYZ` by `*(param_2 + 0x40)` too (B494).</remarks>
    [Test]
    public void Step_ATracerOnTheFirstCall_ScalesByTheTwentiethNotTheStep()
    {
        ParticleEffect effect = new(Launched("move particles between 2 control points", startTime: 0d));

        effect.SetControlPoint(1, Oriented(new Vector3(1000f, 0f, 0f)));
        effect.Step(Oriented(Vector3.Zero), 1f / 66f);

        effect.Particles.PreviousOf(0).X.ShouldBe(-100f * 0.05f, 1e-4f);
    }

    /// <summary>A valid frame at <paramref name="at"/>.</summary>
    private static ParticleControlPoint Oriented(Vector3 at) => new(at, Vector3.UnitX, -Vector3.UnitY, Vector3.UnitZ);

    /// <summary>
    /// One particle at <paramref name="startTime"/>, launched by <paramref name="initializer"/>: a sphere of radius 0 with
    /// ten units a second down local Z, or a tracer at 100 a second toward control point 1. No operators, so nothing moves it after.
    /// </summary>
    private static ParticleSystem Launched(string initializer, double startTime) =>
        new(
            "launched",
            [
                new ParticleFunction("emit_instantaneously", "emit", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["num_to_emit"] = new DmxValue(DmxAttributeType.Whole, 1d),
                    ["emission_start_time"] = new DmxValue(DmxAttributeType.Real, startTime),
                }),
            ],
            [
                new ParticleFunction("Lifetime Random", "life", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["lifetime_min"] = new DmxValue(DmxAttributeType.Real, 10d),
                    ["lifetime_max"] = new DmxValue(DmxAttributeType.Real, 10d),
                }),
                new ParticleFunction(initializer, "launch", new Dictionary<string, DmxValue>(StringComparer.Ordinal)
                {
                    ["speed_in_local_coordinate_system_min"] = new DmxValue(DmxAttributeType.Vector3, Vector: new Vector4(0f, 0f, -10f, 0f)),
                    ["speed_in_local_coordinate_system_max"] = new DmxValue(DmxAttributeType.Vector3, Vector: new Vector4(0f, 0f, -10f, 0f)),
                    ["minimum speed"] = new DmxValue(DmxAttributeType.Real, 100d),
                    ["maximum speed"] = new DmxValue(DmxAttributeType.Real, 100d),
                }),
            ],
            [],
            [],
            [],
            new Dictionary<string, DmxValue>(StringComparer.Ordinal));

    /// <summary>A system with no functions whose definition declares <paramref name="declared"/>.</summary>
    private static ParticleSystem Defined(params (string Name, double Value)[] declared)
    {
        Dictionary<string, DmxValue> definition = new(StringComparer.Ordinal);

        foreach ((string name, double value) in declared)
        {
            definition[name] = new DmxValue(DmxAttributeType.Real, value);
        }

        return new ParticleSystem("p", [], [], [], [], [], definition);
    }
}
