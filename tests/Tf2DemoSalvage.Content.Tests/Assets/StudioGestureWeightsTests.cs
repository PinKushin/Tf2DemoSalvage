using System;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// A sequence's per-bone weight list, read against real sequences of both kinds.
/// </summary>
/// <remarks>
/// **The first version of this test asserted a shape that turned out to be a guess, and the
/// measurement corrected it rather than the other way round.** It expected
/// <c>jumpland_primary</c> to weight some bones and leave others alone, on the reasoning that a
/// gesture restricted to the legs should not move the arms. Measured, every weight there is
/// exactly 1 — and so is every weight for an ordinary run sequence that is never used as a gesture
/// at all, at the IDENTICAL absolute file address despite the two sequences having different
/// relative <c>weightlistindex</c> values from different starting offsets. That is not
/// coincidental: it is <c>studiomdl.exe</c>'s shared default table, written once and pointed at by
/// every sequence whose QC never declared a <c>$weightlist</c>. Two unrelated reads landing on the
/// same address is strong evidence the offset arithmetic is right, not a sign it is broken.
///
/// **A genuinely restricted list does exist in the same file**, which is what this now asserts
/// instead: <c>r_handposes</c> and <c>r_armposes</c> — auxiliary sequences for hand and arm
/// posing — carry real 0/1 patterns rather than the shared default, found by scanning rather than
/// assumed to be there.
/// </remarks>
public sealed class StudioGestureWeightsTests
{
    private const string Game = "F:/SteamLibrary/steamapps/common/Team Fortress 2/tf";

    [Test]
    public void AnUncustomisedSequenceSharesTheDefaultAllOnesTable()
    {
        if (Read("models/player/scout_animations.mdl") is not { } file)
        {
            Assert.Ignore("scout_animations.mdl is not available");
            return;
        }

        // **The animation model's OWN bone count, not the base model's.** `seqdesc.weight(i)` is
        // indexed by the sequence's own local bone numbering — `SlerpBones` remaps it to the base
        // skeleton through `pSeqGroup->boneMap[i]` afterwards, so the weight list itself is sized
        // to whichever file declares the sequence.
        int animationBones = StudioBones.Read(file).Count;

        int jumpLandIndex = IndexOfLabel(file, "jumpland_primary");
        int runIndex = IndexOfActivity(file, "ACT_MP_RUN_PRIMARY");

        float[] jumpLandWeights = StudioGestureWeights.ForSequence(file, jumpLandIndex, animationBones);
        float[] runWeights = StudioGestureWeights.ForSequence(file, runIndex, animationBones);

        jumpLandWeights.Length.ShouldBe(animationBones);

        // Exact, deliberately: studiomdl.exe writes this table as literal 1.0f, not something
        // that approaches one. A tolerance here would also let a genuinely restricted list at
        // 0.999 pass as "the default", which is the one case this test has to distinguish.
#pragma warning disable S1244
        jumpLandWeights.ShouldAllBe(weight => weight == 1f);
#pragma warning restore S1244

        // The measured fact this test exists for: two unrelated sequences that never declared a
        // custom weight list share the identical default, bone for bone.
        jumpLandWeights.ShouldBe(runWeights);
    }

    [Test]
    public void AHandPosingSequenceRestrictsWhichBonesItTouches()
    {
        if (Read("models/player/scout_animations.mdl") is not { } file)
        {
            Assert.Ignore("scout_animations.mdl is not available");
            return;
        }

        int animationBones = StudioBones.Read(file).Count;
        int index = IndexOfLabel(file, "r_handposes");

        float[] weights = StudioGestureWeights.ForSequence(file, index, animationBones);

        weights.Length.ShouldBe(animationBones);

        // The shape that proves this is a REAL restriction rather than the shared default read at
        // a different address by coincidence.
        weights.ShouldContain(weight => weight > 0f, "the sequence must move some bones");
        weights.ShouldContain(weight => weight <= 0f, "and it must leave others alone");

        weights.ShouldAllBe(weight => weight >= 0f && weight <= 1f);
    }

    private static int IndexOfLabel(byte[] file, string label)
    {
        System.Collections.Generic.IReadOnlyList<StudioSequence> sequences = StudioSequences.Read(file);

        for (int index = 0; index < sequences.Count; index++)
        {
            if (sequences[index].Label.Equals(label, StringComparison.OrdinalIgnoreCase))
            {
                return index;
            }
        }

        throw new InvalidOperationException($"no sequence labelled {label}");
    }

    private static int IndexOfActivity(byte[] file, string activity)
    {
        System.Collections.Generic.IReadOnlyList<StudioSequence> sequences = StudioSequences.Read(file);

        for (int index = 0; index < sequences.Count; index++)
        {
            if (sequences[index].Activity == activity)
            {
                return index;
            }
        }

        throw new InvalidOperationException($"no sequence with activity {activity}");
    }

    [Test]
    public void AnAbsentSequenceReturnsNoWeights()
    {
        if (Read("models/player/scout_animations.mdl") is not { } file)
        {
            Assert.Ignore("scout_animations.mdl is not available");
            return;
        }

        // The control: an index past the end must not read garbage out of the next sequence's
        // bytes, which is what happens if the bounds check is missing rather than merely loose.
        StudioGestureWeights.ForSequence(file, sequence: 99_999, boneCount: 250)
            .ShouldBeEmpty();
    }

    [Test]
    public void ZeroBonesReturnsNoWeights()
    {
        if (Read("models/player/scout_animations.mdl") is not { } file)
        {
            Assert.Ignore("scout_animations.mdl is not available");
            return;
        }

        StudioGestureWeights.ForSequence(file, sequence: 0, boneCount: 0).ShouldBeEmpty();
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
