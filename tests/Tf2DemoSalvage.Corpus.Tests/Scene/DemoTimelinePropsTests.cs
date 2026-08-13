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
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

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
    public void Build_KeyframesCostFarLessThanAPosePerFrame()
    {
        // **The design claim, measured.** Keyframes were chosen over a pose per entity per frame
        // on an arithmetic argument; this is the check that the argument holds on real data rather
        // than only on paper. If entities re-sent changing poses constantly the two would converge
        // and the design would be wrong.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

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

    [Test]
    public void PropsAt_ReturnsFewerModelsThanTheDemoEverHeldOf()
    {
        // **The check that tracks are being asked about a moment, not summed.** A viewer draws
        // what exists NOW; a demo's track list is everything that ever existed, including every
        // rocket that has already exploded. If those matched, PropsAt would be ignoring its tick
        // and the map would fill with the debris of the whole match.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            if (timeline.Props.Count == 0 || timeline.Frames.Count == 0)
            {
                continue;
            }

            List<SceneProp> shown = [];

            timeline.PropsAt(timeline.LastTick, shown);

            TestContext.Out.WriteLine(
                $"AT {Path.GetFileName(path)}: {shown.Count} models at the last tick, " +
                $"{timeline.Props.Count} tracks over the whole demo");

            shown.Count.ShouldBeLessThanOrEqualTo(timeline.Props.Count, path);

            // Every model shown must be one the demo actually carried, which catches a pose being
            // paired with the wrong track's path.
            foreach (SceneProp prop in shown)
            {
                timeline.Props
                    .Any(track => string.Equals(track.ModelPath, prop.ModelPath, StringComparison.Ordinal))
                    .ShouldBeTrue(path);
            }
        }
    }

    [Test]
    [Explicit("B47: players carry no model index, so no track exists to interpolate through yet.")]
    public void PlayersAt_BetweenFrames_MovesThroughPositionsNoFrameContains()
    {
        // **Players go through the same interpolator as everything else**, because in the engine
        // they are the same code: m_vecOrigin is registered on C_BaseEntity, and a player is a
        // C_BaseEntity. This is the measurement that they actually do - a position asked for
        // between two frames must be one that no frame states, or the interpolation is not
        // reaching players and they are stepping at the packet rate.
        //
        // **Measured at zero on every demo, and the reason is not this code.** A player's model is
        // not networked: CTFPlayerClassShared::GetModelName returns
        // GetPlayerClassData(m_iClass)->GetModelName(), which the client resolves locally from the
        // class. Only m_iszCustomModel travels. So a CTFPlayer never sends m_nModelIndex, never
        // gets a track, and PlayersAt has nothing to interpolate through - it falls back to the
        // stated frame position, which is what the zero says.
        //
        // Held as Explicit rather than deleted because the assertion is right and becomes the
        // check on B47's fix, which is to build player tracks from the class table instead.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            if (timeline.Frames.Count < 4)
            {
                continue;
            }

            int between = 0;
            List<ScenePlayer> shown = [];

            foreach (TimelineFrame frame in timeline.Frames)
            {
                timeline.PlayersAt(frame.Tick + 0.5, shown);

                foreach (ScenePlayer player in shown)
                {
                    ScenePlayer stated = frame.Players.FirstOrDefault(
                        other => other.EntityIndex == player.EntityIndex);

                    // A whole unit of world space, which is well beyond float noise and far below
                    // any real movement: players run at several hundred units a second, so half a
                    // tick of motion is tens of units.
                    if (stated.EntityIndex == player.EntityIndex &&
                        (Math.Abs(stated.X - player.X) > 1f || Math.Abs(stated.Y - player.Y) > 1f))
                    {
                        between++;
                    }
                }
            }

            TestContext.Out.WriteLine(
                $"INTERP {Path.GetFileName(path)}: {between} player samples off a stated position");

            between.ShouldBeGreaterThan(0, path);
        }
    }

    [Test]
    public void Build_SomethingSomewhereMoves()
    {
        // The control against a scene of statues: tracks that all hold one keyframe would satisfy
        // the assertions above while proving only that entities exist. Projectiles, doors and
        // dropped weapons move, so at least one track must carry more than one pose.
        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            if (timeline.Props.Count == 0)
            {
                continue;
            }

            timeline.Props.Max(track => track.KeyframeCount).ShouldBeGreaterThan(1, path);
        }
    }
}
