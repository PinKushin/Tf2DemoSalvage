using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Turning a networked sequence number into an animation and a frame.
/// </summary>
/// <remarks>
/// **The demo says <c>m_nSequence</c> and <c>m_flCycle</c>, and neither is an animation.** A
/// sequence is a layer above: it names one or more animations, blended by pose parameters, and a
/// cycle is a fraction of the way through it rather than a frame number. Reading a sequence number
/// as an animation index draws whatever animation happens to sit at that slot — plausible motion,
/// entirely wrong.
///
/// The lookup is Valve's, from <c>mstudioseqdesc_t::anim</c>: a short array at
/// <c>animindexindex</c>, indexed <c>y * groupsize[0] + x</c>, with both clamped to the group.
/// </remarks>
public sealed class StudioSequenceTests
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    [Test]
    [TestCase("models/items/medkit_small.mdl")]
    [TestCase("models/items/medkit_medium.mdl")]
    [TestCase("models/player/scout.mdl")]
    public void AModelsSequences_EachNameARealAnimation(string path)
    {
        if (Read(path) is not { } file)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioSequence> sequences = StudioSequences.Read(file);

        sequences.Count.ShouldBeGreaterThan(
            0, "every model that animates declares at least one sequence");

        int animations = StudioAnimation.Count(file);

        foreach (StudioSequence sequence in sequences)
        {
            // **The measurement that a wrong stride cannot survive.** Sequence descriptions are
            // 212 bytes; read at the wrong stride, animindexindex lands in a neighbouring field
            // and the animation index comes out as a bounding-box float reinterpreted as an int.
            sequence.Animation.ShouldBeInRange(0, animations - 1);
        }

        TestContext.Out.WriteLine(
            $"SEQ {Path.GetFileName(path)}: {sequences.Count} sequences, {animations} animations, " +
            $"frames [{string.Join(", ", Enumerable.Range(0, animations).Take(6).Select(a => StudioAnimation.Frames(file, a)))}], " +
            $"first animations [{string.Join(", ", sequences.Take(6).Select(s => s.Animation))}]");
    }

    [Test]
    public void ACycle_PicksTheFrameThatFractionOfTheWayThrough()
    {
        // A cycle is 0..1 across the whole sequence, so the last frame is at cycle 1 and the
        // middle frame at 0.5. Off by one here shows as an animation that never reaches its final
        // pose, which on a looping pickup is invisible - hence an exact prediction rather than a
        // range.
        StudioSequences.FrameFor(cycle: 0f, frames: 31).ShouldBe(0);
        StudioSequences.FrameFor(cycle: 0.5f, frames: 31).ShouldBe(15);
        StudioSequences.FrameFor(cycle: 1f, frames: 31).ShouldBe(30);
    }

    [Test]
    public void ACycleOutsideItsRange_IsWrappedRatherThanClamped()
    {
        // **Wrapped, because a cycle is a phase.** m_flCycle is interpolated between packets and
        // LoopingLerp can carry it just past one; clamping there would stall every looping
        // animation for a frame at its end, which reads as a stutter rather than as a bug.
        StudioSequences.FrameFor(cycle: 1.25f, frames: 41).ShouldBe(10);
        StudioSequences.FrameFor(cycle: -0.25f, frames: 41).ShouldBe(30);
    }

    [Test]
    public void ASingleFrameSequence_IsAlwaysItsOnlyFrame()
    {
        // A static prop's "animation" is one frame, and dividing by frames - 1 there is a division
        // by zero that would send every vertex to NaN and lose the model entirely.
        StudioSequences.FrameFor(cycle: 0f, frames: 1).ShouldBe(0);
        StudioSequences.FrameFor(cycle: 0.7f, frames: 1).ShouldBe(0);
        StudioSequences.FrameFor(cycle: 0.7f, frames: 0).ShouldBe(0);
    }

    [Test]
    public void ALoopingAnimation_NeverDrawsItsDuplicateLastFrame()
    {
        // **STUDIO_LOOPING means "ending frame should be the same as the starting frame"**, in
        // Valve's own words. So a looping animation of 31 frames holds 30 DISTINCT poses, and
        // playing all 31 shows one pose twice - a single frame of hesitation once per loop, which
        // is exactly what an ammo box did after every rotation.
        HashSet<int> seen = [];

        for (int step = 0; step < 600; step++)
        {
            seen.Add(StudioSequences.FrameFor(step / 600f, frames: 31, loops: true));
        }

        seen.ShouldNotContain(30, "frame 30 repeats frame 0, so it must never be drawn");
        seen.Count.ShouldBe(30, "a 31 frame loop holds 30 distinct poses");
    }

    [Test]
    public void ALoopingCycle_ReturnsToItsFirstFrame()
    {
        // The seam itself: the pose just before the end must be followed by the first, with no
        // repeat between them.
        StudioSequences.FrameFor(cycle: 0f, frames: 31, loops: true).ShouldBe(0);
        StudioSequences.FrameFor(cycle: 29f / 30f, frames: 31, loops: true).ShouldBe(29);
        StudioSequences.FrameFor(cycle: 1f, frames: 31, loops: true).ShouldBe(0);
    }

    [Test]
    public void ANonLoopingAnimation_StillEndsOnItsLastFrame()
    {
        // **The control.** A one-shot sequence - a door opening - genuinely ends on its final
        // frame and must hold it. Dropping the last frame for everything would leave every door
        // one frame short of shut.
        StudioSequences.FrameFor(cycle: 1f, frames: 31, loops: false).ShouldBe(30);
    }

    [Test]
    public void AModelWithNoSequences_ReadsAsNone()
    {
        StudioSequences.Read(new byte[512]).ShouldBeEmpty();
        StudioSequences.Read(new byte[8]).ShouldBeEmpty();
    }

    private static byte[]? Read(string path)
    {
        if (!Directory.Exists(Game))
        {
            return null;
        }

        return new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
            .Select(name => Path.Combine(Game, name))
            .Where(File.Exists)
            .Select(VpkArchive.Open)
            .Select(archive => archive.ReadFile(path))
            .FirstOrDefault(found => found is not null);
    }
}
