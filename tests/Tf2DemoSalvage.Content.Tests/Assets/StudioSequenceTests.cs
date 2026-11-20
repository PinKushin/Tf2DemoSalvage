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
    private static string Game => GameInstall.Require();

    [Test]
    [TestCase("models/items/medkit_small.mdl")]
    [TestCase("models/items/medkit_medium.mdl")]
    // **The variants cp_process does not contain.** Every measurement of pack animation so far
    // came from one map, which carries only the small and medium medkit and the small ammo pack.
    // Generalising a bake budget from that is generalising from a sample of three.
    [TestCase("models/items/medkit_large.mdl")]
    [TestCase("models/items/ammopack_small.mdl")]
    [TestCase("models/items/ammopack_medium.mdl")]
    [TestCase("models/items/ammopack_large.mdl")]
    [TestCase("models/player/scout.mdl")]
    [Category(Tf2DemoSalvage.SdkReference.TestCategories.MutationHeavy)]
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
