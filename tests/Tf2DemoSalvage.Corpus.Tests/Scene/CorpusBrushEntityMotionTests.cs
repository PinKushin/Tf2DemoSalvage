using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Core.Tests.Scene;

/// <summary>
/// What a demo says a map's brush entities actually do.
/// </summary>
/// <remarks>
/// **B94's control, and it has to run before anything is changed.** A gate was seen travelling down
/// into the floor rather than up into its frame, and three explanations fit: geometry placed against
/// a pivot the viewer does not apply, an origin belonging to a neighbouring entity, or a mapper who
/// genuinely built a gate that retracts downward. The third is the one where nothing is wrong, and
/// it is settled without any rendering — the demo carries `m_vecOrigin` per tick, so the question
/// "does Z fall" is answered by reading the track.
///
/// Rendering cannot answer it. A door drawn in the wrong place and a door moving the wrong way look
/// identical from a screenshot, which is what makes this worth a test rather than another look.
/// </remarks>
public sealed class CorpusBrushEntityMotionTests
{
    /// <summary>
    /// The exact recording the gate was seen in, named in full rather than by map.
    /// </summary>
    /// <remarks>
    /// **`Corpus.Demo("cp_process")` was wrong here and quietly so.** It returns the FIRST file
    /// whose name contains the fragment, and the local corpus holds two cp_process recordings from
    /// different servers — `br.tf2pickup.org` and `na.serveme.tf #627716`. The viewer was driven on
    /// the second while this test measured whichever sorted first, so the count of moving entities
    /// and the gate that was watched came from different demos. A conclusion drawn across two files
    /// is not a conclusion.
    ///
    /// Both are SourceTV recordings (`SourceTV Demo` in the header), which matters for a different
    /// reason: SourceTV is PVS-limited too, just far less than a POV. A door that moved while the
    /// STV camera was elsewhere is not in the file at all, so "only three moved" may be a fact about
    /// coverage rather than about decoding.
    /// </remarks>
    private const string ProcessDemo = "cp_process_f12-2026-08-08-2207";

    /// <summary>A track's vertical travel across the whole recording.</summary>
    private static float VerticalTravel(ScenePropTrack track)
    {
        IReadOnlyList<(int Tick, ScenePose Pose)> keyframes = track.Keyframes;

        float lowest = keyframes.Min(keyframe => keyframe.Pose.Z);
        float highest = keyframes.Max(keyframe => keyframe.Pose.Z);

        return highest - lowest;
    }

    [Test]
    public void BrushEntitiesThatMove_AreReportedWithTheirDirection()
    {
        string path = Corpus.Demo(ProcessDemo);

        DemoTimeline timeline = TimelineCache.For(path);

        IReadOnlyList<ScenePropTrack> brushes =
            [.. timeline.Props.Where(track => track.Kind == SceneModelKind.Brush)];

        // Control: the demo carries brush entities at all. Without this every claim below is
        // satisfied by a timeline that produced none.
        brushes.ShouldNotBeEmpty();

        // Only the ones that actually travel. A map's brush entities are mostly static -- trigger
        // volumes, non-moving detail brushes -- and a still door says nothing about direction.
        IReadOnlyList<ScenePropTrack> moving =
            [.. brushes.Where(track => VerticalTravel(track) > 1f)];

        // **The measurement this test exists to make**, formatted so a failure reads as a report:
        // which submodel moved, how far, and whether its first keyframe is above or below its last.
        string report = string.Join(
            "; ",
            moving
                .OrderByDescending(VerticalTravel)
                .Select(track => string.Create(
                    CultureInfo.InvariantCulture,
                    $"{track.ModelPath} rest {track.Keyframes[0].Pose.Z:0} " +
                    $"low {track.Keyframes.Min(keyframe => keyframe.Pose.Z):0} " +
                    $"high {track.Keyframes.Max(keyframe => keyframe.Pose.Z):0}")));

        // **The measured answer, which excludes the explanation where nothing is wrong.** Three
        // gates move -- *80, *81 and *186 -- each travelling 145 units between Z 640 and Z 785.
        // The demo says they rise. So a gate seen descending into the floor is this project's
        // defect and not a mapper building a downward gate.
        //
        // Asserted as the range rather than the exact string, because which submodel indices a map
        // gives its gates is not a fact worth pinning: it changes with a recompile and says nothing
        // about whether the motion was read correctly.
        moving.ShouldNotBeEmpty();

        // **The direction, which is the whole question**, expressed without pinning coordinates a
        // recompile would change. A gate rests closed and rises to open, so its resting height is
        // its lowest. One that retracted downward would rest at its highest instead.
        IReadOnlyList<ScenePropTrack> descending =
            [.. moving.Where(track =>
                track.Keyframes[0].Pose.Z >
                track.Keyframes.Min(keyframe => keyframe.Pose.Z) + 1f)];

        descending.ShouldBeEmpty(report);

        // **27 tracks over 13 distinct submodels**, measured on this demo: 78, 80, 81, 132, 135,
        // 137, 139, 141, 143, 144, 146, 185 and 186, resting at 584, 640, 648, 696 or 744 and each
        // rising about 144. Several submodels hold more than one track because a round reset deletes
        // and recreates the entity, which changes its serial number and starts a new track.
        //
        // Pinned as a floor rather than an exact count: the figure is evidence that the demo carries
        // gate motion broadly rather than a property worth breaking a build over.
        moving.Count.ShouldBeGreaterThan(20, report);
    }

    [Test]
    public void BrushEntities_ARecording_AreNeverPlacedAtTheOrigin()
    {
        // **The remaining explanation for a gate in the floor, and it is a decode question.**
        // Delta compression means absent is the DEFAULT, and the default origin is (0,0,0) -- which
        // for a brush entity compiled about its own origin brush places it at world zero, roughly
        // floor level near the middle of the map. A gate that never received m_vecOrigin would sit
        // there and read exactly as "went into the floor" rather than as a missing entity.
        //
        // Instance baselines now reach entering entities, so a gate's spawn origin should arrive
        // even when the snapshot omits it. This is what says whether that is true in practice.
        string path = Corpus.Demo(ProcessDemo);

        DemoTimeline timeline = TimelineCache.For(path);

        IReadOnlyList<ScenePropTrack> brushes =
            [.. timeline.Props.Where(track => track.Kind == SceneModelKind.Brush)];

        brushes.ShouldNotBeEmpty();

        // **Z alone, and getting here took two wrong versions worth recording.** The first asked for
        // X, Y AND Z all near zero, which a door whose Z defaulted while its X and Y stayed real
        // passes cleanly — the test agreed with the defect it was written to catch. Widening it to
        // ANY axis then failed on two entities that are legitimately at X or Y zero, because
        // cp_process is symmetric about the origin and things sit on its centre line.
        //
        // Z is the axis that carries the meaning: it is both the delta-compression default and floor
        // level, so a brush entity at Z zero is either unplaced or in the ground. A door's X and Y
        // being zero says nothing at all.
        IReadOnlyList<string> atOrigin =
            [.. brushes
                .Where(track => track.Keyframes.Any(keyframe => Math.Abs(keyframe.Pose.Z) < 1f))
                .Select(track => track.ModelPath)
                .Distinct()];

        atOrigin.ShouldBeEmpty(string.Join(", ", atOrigin));
    }
}
