using System;
using System.Collections.Generic;
using System.Linq;

using Tf2DemoSalvage.Core.Container;
using Tf2DemoSalvage.Core.Net;
using Tf2DemoSalvage.Core.Schema;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// `OnNewModel` resizes the pose-parameter history when the RECORDING sets the model, seeded from that update (B870 part 2).
/// </summary>
/// <remarks>
/// **The engine's order on one update** (`C_BaseEntity::PostDataUpdate`, `c_baseentity.cpp:2583-2609`): the latch first —
/// `OnLatchInterpolatedVariables( LATCH_ANIMATION_VAR )` appends the update's values — then `ValidateModelIndex()`, which
/// reaches `SetModelPointer` and, when the model pointer differs, `C_BaseAnimating::OnNewModel`
/// (`c_baseentity.cpp:1782-1791`). There `m_iv_flPoseParameter.SetMaxCount( hdr->GetNumPoseParameters() )`
/// (`c_baseanimating.cpp:1124`) wipes the history whenever the count differs from the width and `Reset()` seeds three
/// entries at `curtime` with the values just unpacked (`interpolatedvar.h:740-751`, `:1278-1298`). The width starts at
/// `MAXSTUDIOPOSEPARAM`: `CInterpolatedVarArray`'s constructor calls `SetMaxCount( COUNT )`.
///
/// **What the viewer did instead**: it ran the resize at VIEW time, when a track was first drawn, seeded from the last
/// keyframe of the whole recording and appended after entries that arrived later. Until then the history was one float
/// wide, so every parameter after the first drew the arrival keyframe's value a delay early.
///
/// Synthetic, in `Core.Tests`, with the model's facts handed to the build the way the install hands them (D38).
/// </remarks>
public sealed class ModelResetConformanceTests
{
    private const int PropClassId = 0;

    private const int PropEntityIndex = 7;

    private const string TwoParameters = "models/buildables/sentry1.mdl";

    private const string OtherTwoParameters = "models/buildables/sentry2.mdl";

    private const string OneParameter = "models/props_gameplay/door.mdl";

    /// <summary>
    /// **A model change to a different parameter count resets the history AT THAT UPDATE**, so a target before it draws
    /// the reset's own value: the three reset entries share the update's arrival, and nothing older is in the history.
    /// </summary>
    /// <remarks>
    /// Model A has one parameter and B two. Updates at 0 and 10 under A carry 0.2 and 0.6; at 20 the model becomes B with
    /// (0.9, 0.5). Sampled at 25, the delayed target falls before 20, which the engine's freshly reset history cannot
    /// bracket — `Interpolate` with every entry newer than the target copies the oldest, the reset value. Without the reset
    /// it blends 0.6 toward 0.9.
    /// </remarks>
    [Test]
    public void At_ATargetBeforeAModelChangeThatResized_DrawsTheResetValue()
    {
        DemoTimeline timeline = DemoTimeline.Build(
            Demo((0, 3, [0.2f]), (10, 3, [0.6f]), (20, 1, [0.9f, 0.5f])), models: Facts());

        IReadOnlyList<float> drawn = Track(timeline).At(25d)!.Value.PoseParameters;

        drawn[0].ShouldBe(0.9f, 1e-6f);
        drawn[1].ShouldBe(0.5f, 1e-6f);
    }

    /// <summary>
    /// **The control: a change to a model with the SAME count does not reset**, because `SetMaxCount` wipes only on a width
    /// change — so the same target blends across the change.
    /// </summary>
    [Test]
    public void At_ATargetBeforeAModelChangeOfTheSameWidth_StillBlends()
    {
        DemoTimeline timeline = DemoTimeline.Build(
            Demo((0, 1, [0.2f, 0.4f]), (10, 1, [0.6f, 0.4f]), (20, 2, [0.9f, 0.4f])), models: Facts());

        float drawn = Track(timeline).At(25d)!.Value.PoseParameters[0];

        drawn.ShouldBeInRange(0.61f, 0.89f, "blending 0.6 toward 0.9 across a model change that kept the width");
    }

    /// <summary>
    /// **No facts is the engine's model-less answer**: `OnNewModel` returns before `SetMaxCount` when there is no studio
    /// header (`c_baseanimating.cpp:1042-1072`), so nothing resets and the width stays `MAXSTUDIOPOSEPARAM` — every
    /// parameter blends (part 5: the history was one float wide here, so the second drew the tick-10 value, 0.8).
    /// </summary>
    [Test]
    public void At_AModelWithNoFacts_BlendsEveryParameterUnreset()
    {
        DemoTimeline timeline = DemoTimeline.Build(Demo((0, 1, [0.2f, 0.4f]), (10, 1, [0.6f, 0.8f])));

        IReadOnlyList<float> drawn = Track(timeline).At(13d)!.Value.PoseParameters;

        drawn[0].ShouldBeInRange(0.21f, 0.59f);
        drawn[1].ShouldBe(drawn[0] + 0.2f, 1e-5f);
    }

    /// <summary>
    /// **A window walked with the same facts draws what the whole timeline draws** (B872): the window's builder runs the
    /// same `OnNewModel` at the same update. The control is a window walked without them, which keeps the old history.
    /// </summary>
    [Test]
    public void Window_WalkedWithTheModelFacts_DrawsTheResetAsTheWholeDoes()
    {
        byte[] demo = Demo((0, 3, [0.2f]), (10, 3, [0.6f]), (20, 1, [0.9f, 0.5f]), (30, 1, [0.9f, 0.5f]));
        float whole = Track(DemoTimeline.Build(demo, models: Facts())).At(25d)!.Value.PoseParameters[0];

        float With(IModelFacts? facts)
        {
            TimelineWindows walk = TimelineWindows.Walk(demo, 5, new WindowSettings(), models: facts).ShouldNotBeNull();
            int start = walk.Ticks[walk.IndexAt(25d)];

            return Track(walk.Window(start, 25)).At(25d)!.Value.PoseParameters[0];
        }

        With(Facts()).ShouldBe(whole);
        With(null).ShouldNotBe(whole, "the control: without the facts the window blends across the model change");
    }

    private static ScenePropTrack Track(DemoTimeline timeline) =>
        timeline.Props.Single(track => track.EntityIndex == PropEntityIndex);

    /// <summary>The three models' facts: two parameters, two, and one, none looping.</summary>
    private static FixedFacts Facts() => new FixedFacts(new Dictionary<string, ModelFacts>(StringComparer.Ordinal)
    {
        [TwoParameters] = new([false, false], StaticProp: false, SequenceLoops: null),
        [OtherTwoParameters] = new([false, false], StaticProp: false, SequenceLoops: null),
        [OneParameter] = new([false], StaticProp: false, SequenceLoops: null),
    });

    private sealed class FixedFacts(IReadOnlyDictionary<string, ModelFacts> facts) : IModelFacts
    {
        public ModelFacts? FactsOf(string modelPath) => facts.GetValueOrDefault(modelPath);
    }

    /// <summary>
    /// **An update that sets a static-prop model AND restarts the sequence keeps the cycle's history**: the engine sets the
    /// model before the parity block (`c_baseentity.cpp:2609`, then `c_baseanimating.cpp:4738-4748`), so the block reads the
    /// NEW model's `STUDIOHDR_FLAGS_STATIC_PROP` and skips `m_iv_flCycle.Reset()` — the cycle blends 0.6 toward 0.9.
    /// </summary>
    /// <param name="staticProp">Whether the model set at 20 is a static prop.</param>
    [TestCase(true)]
    [TestCase(false)]
    public void At_AStaticPropModelSetWithASequenceRestart_KeepsTheCycleHistory(bool staticProp)
    {
        FixedFacts facts = new(new Dictionary<string, ModelFacts>(StringComparer.Ordinal)
        {
            [TwoParameters] = new([false, false], StaticProp: false, SequenceLoops: null),
            [OtherTwoParameters] = new([false, false], StaticProp: staticProp, SequenceLoops: null),
        });

        DemoTimeline timeline = DemoTimeline.Build(
            Demo((0, 1, [0.5f, 0.5f], 0.2f, 0), (10, 1, [0.5f, 0.5f], 0.6f, 0), (20, 2, [0.5f, 0.5f], 0.9f, 1)),
            models: facts);

        float cycle = Track(timeline).At(25d)!.Value.Cycle;

        if (staticProp)
        {
            cycle.ShouldBeInRange(0.61f, 0.89f, "the exemption read the new model's flag, so the cycle still blends");
        }
        else
        {
            cycle.ShouldBe(0.9f, "the control: a model that is no static prop resets the cycle to the update's value");
        }
    }

    /// <summary>A demo of one prop, one update per entry: tick, model precache index, pose parameters.</summary>
    private static byte[] Demo(params (int Tick, int ModelIndex, float[] Pose)[] frames) =>
        Demo([.. frames.Select(frame => (frame.Tick, frame.ModelIndex, frame.Pose, (float?)null, (int?)null))]);

    /// <summary>The same, each update also stating a cycle and a new-sequence parity when given.</summary>
    private static byte[] Demo(params (int Tick, int ModelIndex, float[] Pose, float? Cycle, int? Parity)[] frames)
    {
        DemoSchema schema = Schema();

        EntityDecoder decoder = new(schema, EntityDecoder.ClassIdBits(schema.ServerClasses.Count));

        List<DemoCommand> commands =
        [
            SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                0,
                SyntheticDemo.StringTable(
                    ModelPrecache.TableName,
                    [string.Empty, TwoParameters, OtherTwoParameters, OneParameter],
                    maxEntries: 8)),
            SyntheticDemo.DataTables(schema),
        ];

        for (int index = 0; index < frames.Length; index++)
        {
            (int tick, int modelIndex, float[] pose, float? cycle, int? parity) = frames[index];

            IReadOnlyList<FlatProperty> flat = decoder.FlattenedFor(PropClassId);

            List<DecodedProperty> properties =
            [
                Property(flat, "m_nModelIndex", PropertyValue.FromInt(modelIndex)),
                Property(flat, "m_vecOrigin", PropertyValue.FromVectorXY(64f, 0f)),
                Property(flat, "m_vecOrigin[2]", PropertyValue.FromFloat(0f)),
            ];

            for (int parameter = 0; parameter < pose.Length; parameter++)
            {
                properties.Add(Property(flat, $"{parameter:000}", PropertyValue.FromFloat(pose[parameter])));
            }

            if (cycle is { } stated)
            {
                properties.Add(Property(flat, "m_flCycle", PropertyValue.FromFloat(stated)));
            }

            if (parity is { } counter)
            {
                properties.Add(Property(flat, "m_nNewSequenceParity", PropertyValue.FromInt(counter)));
            }

            properties.Sort((left, right) => left.Index.CompareTo(right.Index));

            DecodedEntity prop = new(
                PropEntityIndex,
                PropClassId,
                SerialNumber: 3,
                index == 0 ? EntityUpdateType.Enter : EntityUpdateType.Delta,
                properties);

            byte[] body = decoder.EncodeEntities([prop], [], isDelta: index > 0, 0, out int bits);

            commands.Add(SyntheticDemo.Packet(
                SyntheticDemo.DefaultProtocol,
                tick,
                new PacketEntitiesMessage(
                    MaxEntries: 64,
                    IsDelta: index > 0,
                    DeltaFromTick: index > 0 ? frames[index - 1].Tick : null,
                    BaselineIndex: false,
                    UpdatedEntries: 1,
                    LengthBits: bits,
                    UpdateBaseline: false,
                    Body: body)));
        }

        return SyntheticDemo.From(SyntheticDemo.DefaultProtocol, [.. commands]);
    }

    private static DecodedProperty Property(IReadOnlyList<FlatProperty> flat, string name, PropertyValue value)
    {
        for (int candidate = 0; candidate < flat.Count; candidate++)
        {
            if (string.Equals(flat[candidate].Property.Name, name, StringComparison.Ordinal))
            {
                return new DecodedProperty(candidate, flat[candidate], value);
            }
        }

        throw new InvalidOperationException($"the fixture schema declares no {name}");
    }

    /// <summary>One animating prop class: a model index, a position, and a two-element pose-parameter array.</summary>
    private static DemoSchema Schema() => new(
        [
            new SendTable("DT_BaseEntity", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.Int, "m_nModelIndex", 1, string.Empty, 0f, 0f, 13, 0),
                new SendProperty(SendPropType.VectorXY, "m_vecOrigin", 1, string.Empty, -16384f, 16384f, 32, 0),
                new SendProperty(SendPropType.Float, "m_vecOrigin[2]", 1, string.Empty, -16384f, 16384f, 32, 0),
            ]),
            new SendTable("m_flPoseParameter", NeedsDecoder: false,
            [
                new SendProperty(SendPropType.Float, "000", 1, string.Empty, 0f, 1f, 32, 0),
                new SendProperty(SendPropType.Float, "001", 1, string.Empty, 0f, 1f, 32, 0),
            ]),
            new SendTable("DT_BaseAnimating", NeedsDecoder: false,
            [
                new SendProperty(SendPropType.Int, "m_nNewSequenceParity", 1, string.Empty, 0f, 0f, 3, 0),
            ]),
            new SendTable("DT_ServerAnimationData", NeedsDecoder: false,
            [
                new SendProperty(SendPropType.Float, "m_flCycle", 1, string.Empty, 0f, 1f, 32, 0),
            ]),
            new SendTable("DT_DynamicProp", NeedsDecoder: true,
            [
                new SendProperty(SendPropType.DataTable, "baseentity", 1, "DT_BaseEntity", 0f, 0f, 0, 0),
                new SendProperty(SendPropType.DataTable, "m_flPoseParameter", 1, "m_flPoseParameter", 0f, 0f, 0, 0),
                new SendProperty(SendPropType.DataTable, "baseanimating", 1, "DT_BaseAnimating", 0f, 0f, 0, 0),
                new SendProperty(SendPropType.DataTable, "serveranimdata", 1, "DT_ServerAnimationData", 0f, 0f, 0, 0),
            ]),
        ],
        [new ServerClass(PropClassId, "CDynamicProp", "DT_DynamicProp")]);
}
