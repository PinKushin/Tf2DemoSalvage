using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A view's bone setups decide a real demo's cycle resets (B878), read at the output: the props the timeline hands out.</summary>
/// <remarks>
/// **The output-level assertion the synthetic tests cannot make** (CLAUDE.md): `z1800`, a real match, played tick by tick
/// through 3,000 ticks of its middle, three ways — with no view, with a view that sets up every prop every frame, and with
/// a view that sets up nothing. Posed every frame, the engine sets bones up after every update, so only a parity change
/// resets — the history the timeline stores — and the props must be the no-view props exactly. Never posed, every update
/// resets, and some prop's cycle must differ: the control that the view's path runs on real data at all.
/// </remarks>
public sealed class BoneSetupCorpusTests
{
    private const int Window = 3000;

    /// <summary>Ticks posed before the comparison starts — many interpolation windows.</summary>
    private const int WarmUp = 200;

    [Test]
    public void PropsAt_ARealMatchPosedEveryFrame_IsTheNoViewAnswerAndNeverPosedIsNot()
    {
        byte[] bytes = File.ReadAllBytes(Corpus.Demo("z1800"));
        DemoTimeline plain = DemoTimeline.Build(bytes);
        DemoTimeline posed = DemoTimeline.Build(bytes);
        DemoTimeline unposed = DemoTimeline.Build(bytes);
        BoneSetups posedSetups = new();
        BoneSetups unposedSetups = new();
        List<SceneProp> plainProps = [];
        List<SceneProp> posedProps = [];
        List<SceneProp> unposedProps = [];
        HashSet<int> everyProp = [];
        int start = plain.FirstTick + ((plain.LastTick - plain.FirstTick - Window) / 2);
        int compared = 0;
        int mismatched = 0;
        int differentUnposed = 0;

        for (int tick = start; tick < start + Window; tick++)
        {
            plain.PropsAt(tick, plainProps);
            posed.PropsAt(tick, posedProps, setups: posedSetups);
            unposed.PropsAt(tick, unposedProps, setups: unposedSetups);

            Dictionary<int, float> expected = plainProps.ToDictionary(prop => prop.EntityIndex, prop => prop.Pose.Cycle);

            // **After a warm-up**: a prop alive before the view's first frame has no setup the view saw, so its first frame
            // is a never-posed one and its history starts at its newest update for an interpolation window — the engine's
            // answer for a view that has just begun, and measured as one sentry for 13 ticks before this was here.
            for (int at = 0; tick >= start + WarmUp && at < posedProps.Count; at++)
            {
                SceneProp prop = posedProps[at];
                compared++;

                if (!expected.TryGetValue(prop.EntityIndex, out float cycle) || cycle.CompareTo(prop.Pose.Cycle) != 0)
                {
                    mismatched++;
                    TestContext.Out.WriteLine(
                        $"MISMATCH {tick} entity {prop.EntityIndex} {prop.ModelPath} posed {prop.Pose.Cycle} plain " +
                        $"{(expected.TryGetValue(prop.EntityIndex, out float shown) ? shown : float.NaN)}");
                }
            }

            differentUnposed += unposedProps.Count(prop =>
                expected.TryGetValue(prop.EntityIndex, out float cycle) && cycle.CompareTo(prop.Pose.Cycle) != 0);

            everyProp.Clear();
            everyProp.UnionWith(posedProps.Select(prop => prop.EntityIndex));
            posed.NoteBonesSetUp(posedSetups, everyProp, tick);
        }

        TestContext.Out.WriteLine($"B878 z1800 {start}..{start + Window}: {compared} compared, {mismatched} mismatched, " +
            $"{differentUnposed} differ never posed");

        compared.ShouldBeGreaterThan(0, "the control: the window holds props");
        mismatched.ShouldBe(0);
        differentUnposed.ShouldBeGreaterThan(0, "the control: never posed, some cycle reset the stored history did not");
    }
}
