using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// The model-bearing entities a demo carries, across every era in the corpus.
/// </summary>
/// <remarks>
/// **The measurement the unit tests cannot make.** Everything underneath is checked against
/// hand-built states, which can only confirm that the code does what it was written to do. Whether
/// <c>m_nModelIndex</c> resolves through <c>modelprecache</c> on a 2007 demo *and* on a 2026 one is
/// a question only real files answer — and it is exactly the question this project exists to get
/// right, since the packing quirk in <c>ModelPrecache.Unpack</c> applies to the old ones alone.
/// </remarks>
public sealed class DemoTimelinePropsTests
{
    [Test]
    public void Build_FindsModelsOnEveryEra()
    {
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = TimelineCache.For(path);

            int keyframes = timeline.Props.Sum(track => track.KeyframeCount);
            int distinct = timeline.Props
                .Select(track => track.ModelPath)
                .Distinct(StringComparer.Ordinal)
                .Count();

            TestContext.Out.WriteLine(
                $"PROPS {Path.GetFileName(path)}: {timeline.Props.Count} tracks, " +
                $"{distinct} distinct models, {keyframes} keyframes");

            // Every TF2 match has world entities with models in it - weapons at the very least,
            // since every player carries one. A demo that produces none has had its model indices
            // resolved to nothing, which is silent: the scene simply stays empty.
            timeline.Props.Count.ShouldBeGreaterThan(0, path);

            // **Three kinds of model live in one table, and this test found two of them.** Written
            // first to demand "models/", it failed on "*3" from the 2007 demo - a leading asterisk
            // is an inline BSP submodel, which the engine tracks as mod_brush - and then on
            // "sprites/light_glow02_noz.vmt" from the 2008 one, which is mod_sprite. Valve's
            // modtype_t had all three the whole time; the corpus is what made us read it.
            //
            // Kept as an assertion rather than turned into a default, because that is what caught
            // them: an unrecognised reference handed to a .mdl loader draws nothing and says
            // nothing, and a fourth kind on some era we have not measured would do the same.
            foreach (ScenePropTrack track in timeline.Props)
            {
                track.Kind.ShouldNotBe(
                    SceneModelKind.Unknown,
                    $"{path}: unexpected model reference '{track.ModelPath}'");
            }
        }
    }

    [Test]
    public void Props_TheWorldEntity_IsNeverATrack()
    {
        // **The output-level assertion for the world exclusion**, which no unit test can make:
        // whether entity zero reaches the prop list is a fact about a real demo's first packet.
        //
        // It got there the moment instance baselines were applied (B132). CWorld states its model
        // index once, in its class baseline, so before that fix it was an entity with no properties
        // at all; afterwards it holds model index 1 — `maps/<name>.bsp` — and became a prop track
        // covering the whole map. C_BaseEntity::ShouldDraw ends `&& (index != 0)`, at
        // c_baseentity.cpp:1450, and that is the rule this checks.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = TimelineCache.For(path);

            timeline.Props.ShouldNotContain(
                track => track.EntityIndex == 0,
                $"{path}: entity zero is the world and is drawn by the map, not as a prop");

            // Stated twice on purpose, because the two catch different mistakes: an index check
            // survives a renamed model and a model check survives some other entity acquiring the
            // world's index. A `.bsp` in the prop list is the world however it got there.
            timeline.Props.ShouldNotContain(
                track => track.ModelPath.EndsWith(".bsp", StringComparison.OrdinalIgnoreCase),
                $"{path}: a map file reached the prop list");
        }
    }

    [Test]
    public void Build_KeyframesCostFarLessThanAPosePerFrame()
    {
        // **The design claim, measured.** Keyframes were chosen over a pose per entity per frame
        // on an arithmetic argument; this is the check that the argument holds on real data rather
        // than only on paper. If entities re-sent changing poses constantly the two would converge
        // and the design would be wrong.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = TimelineCache.For(path);

            if (timeline.Props.Count == 0 || timeline.Frames.Count == 0)
            {
                continue;
            }

            long keyframes = timeline.Props.Sum(track => (long)track.KeyframeCount);
            long perFrame = (long)timeline.Props.Count * timeline.Frames.Count;

            TestContext.Out.WriteLine(
                $"COST {Path.GetFileName(path)}: {keyframes} keyframes against " +
                $"{perFrame} for a pose per track per frame");

            keyframes.ShouldBeLessThan(perFrame, path);
        }
    }



}
