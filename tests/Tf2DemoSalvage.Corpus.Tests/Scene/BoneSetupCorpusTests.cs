using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>A view's bone setups decide a real demo's cycle resets (B878), read at the output: the props the timeline hands out.</summary>
/// <remarks>
/// **The output-level assertion the synthetic tests cannot make** (CLAUDE.md): `z1800`, a real match, played tick by tick
/// through its first 3,000 ticks, three ways — with no view, with a view that sets up every drawn prop every frame, and
/// with a view that sets up nothing. A prop set up at every update since its creation is reset only where its parity
/// changes — the history the timeline stores — so for every prop drawn CONTINUOUSLY from the first tick, the props must
/// be the no-view props exactly. A prop hidden on the way loses its setups in the engine
/// too, so it is not compared (B878 review 2: a fixed warm-up hid that, and hid it only on this window's data). Never
/// posed, every update resets, and some prop's cycle must differ: the control that the view's path runs on real data.
/// </remarks>
public sealed class BoneSetupCorpusTests
{
    private const int Window = 3000;

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
        HashSet<int> drawn = [];
        // **From the first tick**, so every prop is set up from its creation: a view that begins later has, for a prop alive
        // then, seen nothing — its history starts at the newest update, the engine's answer for a view just begun (measured:
        // one sentry, 13 ticks, when this began mid-demo).
        int start = plain.FirstTick;
        int compared = 0;
        int mismatched = 0;
        int differentUnposed = 0;
        HashSet<int>? continuous = null;

        for (int tick = start; tick < start + Window; tick++)
        {
            plain.PropsAt(tick, plainProps);
            posed.PropsAt(tick, posedProps, setups: posedSetups);
            unposed.PropsAt(tick, unposedProps, setups: unposedSetups);

            Dictionary<int, float> expected = plainProps.ToDictionary(prop => prop.EntityIndex, prop => prop.Pose.Cycle);

            drawn.Clear();
            drawn.UnionWith(posedProps.Select(prop => prop.EntityIndex));
            continuous ??= [.. drawn];
            continuous.IntersectWith(drawn);

            foreach (SceneProp prop in posedProps.Where(prop => continuous.Contains(prop.EntityIndex)))
            {
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

            posed.NoteBonesSetUp(posedSetups, drawn, tick);
        }

        TestContext.Out.WriteLine($"B878 z1800 {start}..{start + Window}: {compared} compared, {mismatched} mismatched, " +
            $"{differentUnposed} differ never posed, {continuous?.Count} props drawn throughout");

        compared.ShouldBeGreaterThan(0, "the control: the window holds props drawn throughout");
        mismatched.ShouldBe(0);
        differentUnposed.ShouldBeGreaterThan(0, "the control: never posed, some cycle reset the stored history did not");
    }
}
