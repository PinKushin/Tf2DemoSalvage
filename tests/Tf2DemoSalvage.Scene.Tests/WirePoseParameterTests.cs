using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// The pose parameters an entity sends reach the blend that uses them.
/// </summary>
/// <remarks>
/// **The decode was never the hard half — the wiring was** (B269). `m_flPoseParameter` arrives on
/// every animating entity that is not a player, and `EntityModelSet.PoseValues` filled the array
/// exclusively from `CBasePlayerAnimState`'s four computed parameters, so a sentry gun's `aim_pitch`
/// and `aim_yaw` were whatever an uncomputed parameter falls to.
///
/// **Which is mid-range, not zero, and that is why nobody noticed.** `Filled` leaves an uncomputed
/// parameter at a RAW zero and normalises it afterwards, so a symmetric −50..50 becomes 0.5 — dead
/// centre. Every sentry in every demo drew level and pointing straight ahead: a pose that looks
/// entirely reasonable in a screenshot and is simply never the one it was tracking.
///
/// These assert on <c>PoseValuesOf</c>, which reports the array the skeleton was posed with —
/// carried from where it was produced rather than recomputed here.
/// </remarks>
public sealed class WirePoseParameterTests
{
    /// <summary>A sentry's own two, as the model probe reports them from `sentry3.mdl`.</summary>
    private static StudioPoseParameter[] SentryParameters =>
        [
            new("aim_pitch", -50f, 50f, 0f),
            new("aim_yaw", -180f, 180f, 360f),
        ];

    [Test]
    public void PoseValues_ForAnEntityThatSentThem_AreTheWiresOwn()
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Building(sent: [0.75f, 0.25f])];

        models.Add(props, Sentry);
        models.Instances(props, instances);

        IReadOnlyList<float> values = models.PoseValuesOf(1);

        values.Count.ShouldBe(2);
        values[0].ShouldBe(0.75f);
        values[1].ShouldBe(0.25f);
    }

    /// <remarks>
    /// **The control, and it is what the defect looked like.** An entity that sends nothing must
    /// not be given the wire's answer — and before this existed EVERY entity took this path, which
    /// is why the failure was invisible: the values were all present, all zero, and zero is legal.
    /// </remarks>
    [Test]
    public void PoseValues_ForAnEntityThatSentNone_AreNotTakenFromTheWire()
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Building(sent: [])];

        models.Add(props, Sentry);
        models.Instances(props, instances);

        IReadOnlyList<float> values = models.PoseValuesOf(1);

        // **0.5, not 0, and the difference is the whole reason this defect was invisible.** `Filled`
        // leaves an uncomputed parameter at a RAW zero and then normalises it, so a symmetric range
        // like −50..50 lands in the MIDDLE. A sentry therefore drew level and straight ahead — a
        // perfectly plausible pose — rather than at some obviously broken extreme that somebody
        // would have reported years ago.
        values.Count.ShouldBe(2);
        values[0].ShouldBe(0.5f);
        values[1].ShouldBe(0.5f);
    }

    /// <remarks>
    /// **More values than the model has parameters is the ORDINARY case, not a malformed one.** The
    /// server's array is a fixed 24 slots (`MAXSTUDIOPOSEPARAM`) and a sentry uses two, so anything
    /// that copied the wire's length would hand the blend a 24-long array indexed against a 2-long
    /// parameter list.
    /// </remarks>
    [Test]
    public void PoseValues_WithMoreSentThanTheModelDeclares_KeepTheModelsLength()
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Building(sent: [0.1f, 0.2f, 0.3f, 0.4f, 0.5f])];

        models.Add(props, Sentry);
        models.Instances(props, instances);

        IReadOnlyList<float> values = models.PoseValuesOf(1);

        values.Count.ShouldBe(2, "the model has two parameters however many slots the wire sent");
        values[0].ShouldBe(0.1f);
        values[1].ShouldBe(0.2f);
    }

    [Test]
    public void PoseValues_WithFewerSentThanTheModelDeclares_LeaveTheRestAtZero()
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];

        SceneProp[] props = [Building(sent: [0.6f])];

        models.Add(props, Sentry);
        models.Instances(props, instances);

        IReadOnlyList<float> values = models.PoseValuesOf(1);

        values.Count.ShouldBe(2);
        values[0].ShouldBe(0.6f);
        values[1].ShouldBe(0f);
    }

    /// <remarks>
    /// **`OnNewModel`'s pose-parameter half, asserted end to end.** The model says `aim_yaw` loops
    /// and `aim_pitch` does not, and this is the one place that fact leaves the model — a callback
    /// the window wires to the demo's interpolator, since interpolation runs a layer below models.
    /// Without it a sentry crossing due south sweeps the long way round for a whole interpolation
    /// window.
    /// </remarks>
    [Test]
    public void ModelResolved_ForAModelWithALoopingParameter_ReportsWhichOneLoops()
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];
        List<(int Entity, IReadOnlyList<bool> Looping)> told = [];

        models.ModelResolved = (entity, looping) => told.Add((entity, looping));

        SceneProp[] props = [Building(sent: [0.5f, 0.5f])];

        models.Add(props, Sentry);
        models.Instances(props, instances);

        told.Count.ShouldBe(1, "the model became known for exactly one entity");
        told[0].Entity.ShouldBe(1);
        told[0].Looping.Count.ShouldBe(2);
        told[0].Looping[0].ShouldBeFalse("aim_pitch does not wrap — it stops at ±50");
        told[0].Looping[1].ShouldBeTrue("aim_yaw wraps, which is what loop 360 says");
    }

    /// <remarks>
    /// **The control for the one above.** A model whose parameters all stop at their ends must
    /// report an empty list, not two falses — and a fixture that only ever asked a looping model
    /// could not tell a correct answer from one that says everything loops.
    /// </remarks>
    [Test]
    public void ModelResolved_ForAModelWithNoLoopingParameter_ReportsNone()
    {
        EntityModelSet models = new();
        List<ModelInstance> instances = [];
        List<(int Entity, IReadOnlyList<bool> Looping)> told = [];

        models.ModelResolved = (entity, looping) => told.Add((entity, looping));

        SceneProp[] props = [Building(sent: [0.5f])];

        models.Add(props, _ => Frames(SyntheticSkinnedModel.WithPoseParameters(
            new StudioPoseParameter("aim_pitch", -50f, 50f, 0f))));

        models.Instances(props, instances);

        told.Count.ShouldBe(1);
        told[0].Looping.ShouldBeEmpty();
    }

    /// <summary>A building-like prop carrying the pose parameters given.</summary>
    private static SceneProp Building(float[] sent) =>
        new(
            1,
            "models/buildables/sentry3.mdl",
            ScenePropTrack.Classify("models/buildables/sentry3.mdl"),
            new ScenePose { PoseParameters = sent },
            null);

    /// <summary>The sentry's model, skinned, with its two pose parameters.</summary>
    private static PropModels.ModelFrames Sentry(string path) =>
        Frames(SyntheticSkinnedModel.WithPoseParameters(SentryParameters));

    /// <summary>One triangle around a skinned model, which is all these cases need drawn.</summary>
    private static PropModels.ModelFrames Frames(PropModels.SkinnedModel skinned) =>
        new(
            [
                new PropVertex[]
                {
                    new(1f, 0f, 0f, 0f, 0f, MaterialIndex: 3),
                    new(0f, 1f, 0f, 1f, 0f, MaterialIndex: 3),
                    new(0f, 0f, 1f, 0f, 1f, MaterialIndex: 3),
                },
            ],
            new Dictionary<int, (int Start, int Frames, float CyclesPerSecond)> { [0] = (0, 1, 0f) },
            [0],
            [true],
            Skinned: skinned);
}
