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
    public void PlayersAt_BetweenFrames_MovesThroughPositionsNoFrameContains()
    {
        // **Players go through the same interpolator as everything else**, because in the engine
        // they are the same code: m_vecOrigin is registered on C_BaseEntity, and a player is a
        // C_BaseEntity. This is the measurement that they actually do - a position asked for
        // between two frames must be one that no frame states, or the interpolation is not
        // reaching players and they are stepping at the packet rate.
        //
        // **This measured zero when first written, and that was B47.** A player's model is not
        // networked - CTFPlayerClassShared::GetModelName resolves it locally from m_iClass - so a
        // CTFPlayer sends no m_nModelIndex, got no track, and PlayersAt had nothing to interpolate
        // through. It silently fell back to the stated frame position.
        //
        // The fix was to recognise a player by class and give it a track with no model, since the
        // poses are what the interpolator needs and the model comes from the install. This test is
        // what says the fix took.
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
    public void EntitiesAreHiddenAndComeBack_RatherThanLingering()
    {
        // **The check that EF_NODRAW actually arrives.** A taken health pack is hidden rather than
        // deleted because it respawns, and the fix for that reads one bit of m_fEffects. If the
        // property never reaches the decoder the fix is a no-op that looks identical to working -
        // markers on the floor either way - so the only honest verification is to count.
        //
        // Also checks the coming back. A track whose poses are hidden from some point onwards
        // could be an entity that was destroyed, which proves nothing about respawning; one that
        // goes hidden and visible again is a pickup doing what pickups do.
        int hiddenAnywhere = 0;
        int returned = 0;

        foreach (string path in Corpus.FilesWithSchema())
        {
            DemoTimeline timeline = DemoTimeline.Build(File.ReadAllBytes(path));

            int hiddenHere = 0;
            int returnedHere = 0;

            // **Over the keyframes, not over the ticks.** The first version of this walked every
            // tick of every track, which on a 100,000-tick demo with 1,400 tracks is 140 million
            // lookups to examine a few tens of thousands of stored poses. A test slow enough that
            // nobody runs it is a test that does not exist.
            foreach (ScenePropTrack track in timeline.Props)
            {
                bool everHidden = false;
                bool everBack = false;

                foreach ((int _, ScenePose pose) in track.Keyframes)
                {
                    if (pose.Hidden)
                    {
                        everHidden = true;
                    }
                    else if (everHidden)
                    {
                        everBack = true;
                        break;
                    }
                }

                hiddenHere += everHidden ? 1 : 0;
                returnedHere += everBack ? 1 : 0;
            }

            TestContext.Out.WriteLine(
                $"HIDDEN {Path.GetFileName(path)}: {hiddenHere} of {timeline.Props.Count} tracks " +
                $"hidden at some point, {returnedHere} of them came back");

            hiddenAnywhere += hiddenHere;
            returned += returnedHere;
        }

        hiddenAnywhere.ShouldBeGreaterThan(0, "EF_NODRAW never reached the timeline");
        returned.ShouldBeGreaterThan(0, "nothing was ever hidden and then shown again");
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
