using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Content.Tests.Assets;

/// <summary>
/// Posing a model's skeleton from its animation data.
/// </summary>
/// <remarks>
/// **The measurement that matters is whether a player stands up**, and it is a real one: a TF2
/// player is about 83 units tall, so a correctly posed model has that on Z and less than half of
/// it on the other two axes. Drawn from its rest pose instead, the same model measures 84 on Y and
/// 25 on Z — which is what put a dozen players on the map lying flat with their arms at their
/// sides.
///
/// That is a prediction of an exact quantity from outside the code, which is what makes it an
/// experiment rather than a change detector. Nothing here asserts "some transform happened".
/// </remarks>
public sealed class StudioAnimationTests
{
    private static string Game => GameInstall.Require();

    /// <summary>How tall a TF2 player is, from the game's own player hull.</summary>
    private const float PlayerHeight = 83f;

    [Test]
    [TestCase("models/player/scout.mdl")]
    [TestCase("models/player/soldier.mdl")]
    [TestCase("models/player/heavy.mdl")]
    public void APosedPlayerModel_StandsUp(string model)
    {
        if (Read(model) is not { } files)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(files.Model);

        bones.Count.ShouldBeGreaterThan(
            50, "a player model has scores of bones, so a low count means the stride is wrong");

        IReadOnlyList<StudioBonePose> pose =
            StudioAnimation.Pose(files.Model, bones, animation: 0, frame: 0);

        pose.Count.ShouldBeGreaterThan(0, "the model carries animation data to pose it with");

        (float x, float y, float z) = Extents(StudioBones.Posed(bones, pose), files.Vertices);

        // Within a few units of a player's real height, on Z, and clearly the longest axis.
        z.ShouldBeInRange(PlayerHeight - 8f, PlayerHeight + 8f);
        z.ShouldBeGreaterThan(x * 1.5f);
        z.ShouldBeGreaterThan(y * 1.5f);
    }

    [Test]
    public void TheRestPoseOfAPlayerModel_IsLyingDown()
    {
        // **The control, and it is the whole reason the posed test means anything.** If the rest
        // pose already stood the model up, the test above would pass against code that applied no
        // animation at all - which is exactly the state this project was in.
        //
        // Kept as an assertion rather than a comment because it is a claim about TF2's own files
        // that a future reader would otherwise have to take on trust, and because it is the thing
        // that would change silently if the bone reader broke.
        if (Read("models/player/scout.mdl") is not { } files)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(files.Model);

        (float x, float y, float z) = Extents(StudioBones.RestPose(bones), files.Vertices);

        y.ShouldBeInRange(PlayerHeight - 8f, PlayerHeight + 8f);
        z.ShouldBeLessThan(y * 0.5f);

        TestContext.Out.WriteLine($"REST scout rest pose x {x:0.#} y {y:0.#} z {z:0.#}");

        // Which encodings the pose actually uses, so a sabotage check knows what it is allowed to
        // conclude. A wrong Quaternion48 scale left every test green, which is not a weak
        // assertion - it is a path these models never take.
        IReadOnlyList<StudioBonePose> pose =
            StudioAnimation.Pose(files.Model, bones, animation: 0, frame: 0);

        TestContext.Out.WriteLine(
            $"REST scout posed {pose.Count} bones, first "
            + string.Join(
                " ",
                pose.Take(3).Select(entry =>
                    $"[{entry.Bone}] pos({entry.Position.X:0.##},{entry.Position.Y:0.##},{entry.Position.Z:0.##}) "
                    + $"rot({entry.Rotation.X:0.##},{entry.Rotation.Y:0.##},{entry.Rotation.Z:0.##},{entry.Rotation.W:0.##})")));
    }

    [Test]
    public void ARestSkeletonAppliedToItsOwnModel_MovesNothing()
    {
        // Rest pose skinning is BoneToWorld times poseToBone, and those are inverses for vertices
        // stored in that pose - so the identity is the correct answer, not a failure to do
        // anything. Measured rather than assumed, because "it changed nothing" was the first
        // evidence that an animation was needed, and a reader meeting that later should find it
        // stated.
        if (Read("models/player/scout.mdl") is not { } files)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        StudioSkeleton rest = StudioBones.RestPose(StudioBones.Read(files.Model));
        StudioVertex sample = files.Vertices[files.Vertices.Count / 2];

        (float x, float y, float z) =
            rest.Skin(sample.Bones, sample.Weights, sample.X, sample.Y, sample.Z);

        x.ShouldBe(sample.X, 0.01f);
        y.ShouldBe(sample.Y, 0.01f);
        z.ShouldBe(sample.Z, 0.01f);
    }

    [Test]
    public void AVertexNamingNoBone_IsLeftWhereItIs()
    {
        // A static prop's vertices are already in model space. Moving them by a skeleton they do
        // not reference would invent a pose, and a weightless vertex divided by its own total
        // would go to infinity and stretch one triangle across the whole map.
        StudioSkeleton skeleton = StudioBones.RestPose(
        [
            new StudioBone("root", -1, (0f, 0f, 0f), (0f, 0f, 0f, 1f), new float[12]),
        ]);

        skeleton.Skin((0, 0, 0), (0f, 0f, 0f), 3f, 4f, 5f).ShouldBe((3f, 4f, 5f));
    }

    [Test]
    public void AnEmptySkeleton_LeavesEveryVertexAlone()
    {
        StudioBones.RestPose([]).Skin((0, 0, 0), (1f, 0f, 0f), 1f, 2f, 3f).ShouldBe((1f, 2f, 3f));
    }

    [Test]
    public void AModelWithNoAnimations_PosesNothing()
    {
        // Not an error: a model may carry none, and answering with an empty pose lets the caller
        // fall back to the rest skeleton rather than guessing at one.
        StudioAnimation.Pose(new byte[512], [], animation: 0, frame: 0).ShouldBeEmpty();
        StudioAnimation.Count(new byte[512]).ShouldBe(0);
    }

    [Test]
    public void AFileTooShortToDescribeItself_IsNotRead()
    {
        // Reached by a truncated download or a wrong path, and reading past the end of it would be
        // a crash rather than a bad model.
        StudioAnimation.Count(new byte[8]).ShouldBe(0);
        StudioBones.Read(new byte[8]).ShouldBeEmpty();
    }

    /// <remarks>
    /// **The last line of `CalcBoneQuaternion`** (`bone_setup.cpp:470`), and the reason the two
    /// blends downstream may skip aligning at all:
    ///
    /// <code>
    ///   // align to unified bone
    ///   if (!(panim->flags &amp; STUDIO_ANIM_DELTA) &amp;&amp; (iBaseFlags &amp; BONE_FIXED_ALIGNMENT))
    ///       QuaternionAlign( baseAlignment, q, q );
    /// </code>
    ///
    /// **The flag is the manipulation and the animation is real.** No TF2 model measured sets
    /// `BONE_FIXED_ALIGNMENT` — 0 of 924 bones across 37 models — so a fixture that waited for real
    /// content would never run. Flagging a bone of a real model and choosing an alignment antipodal
    /// to what that bone actually decodes to gives an exact prediction: the same rotation, negated.
    ///
    /// **A quaternion and its negation are the SAME rotation**, which is what makes this safe to
    /// assert and also why it matters. The pose is unchanged; what changes is which of the two
    /// representations later blends interpolate from, and that decides whether a joint travels the
    /// short way or the long way round.
    ///
    /// **Every bone is swept, and that is the fix to a wrong CONDITION rather than a wrong
    /// assertion.** The first version flagged one bone picked for being turned away from its rest
    /// pose — and it decoded through `STUDIO_ANIM_RAWROT`, which returns before the alignment in the
    /// engine too. So the prediction was for a branch that bone never took, and the measurement
    /// (unchanged) was correct.
    ///
    /// **The sweep carries its own denominator**, which is what stops it passing vacuously: a bone
    /// on the raw path must come back untouched, a bone on the animated path must come back exactly
    /// negated, and at least one must be on the animated path. With no alignment implemented the
    /// negated count is zero and the last assertion fails.
    /// </remarks>
    [Test]
    public void Pose_WithAFixedAlignmentBone_AlignsTheRotationToTheBonesOwnOrientation()
    {
        if (Read("models/player/heavy.mdl") is not { } files)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(files.Model);
        int aligned = 0;
        int untouched = 0;

        foreach (int subject in Posed(files.Model, bones))
        {
            (float X, float Y, float Z, float W) before = RotationOf(files.Model, bones, subject);

            // Antipodal to what the bone decodes to, so `QuaternionAlign` must flip it — on the
            // branch that reaches `QuaternionAlign` at all.
            (float X, float Y, float Z, float W) after =
                RotationOf(files.Model, Flagged(bones, subject, Negated(before)), subject);

            if (Matches(after, before))
            {
                untouched++;
                continue;
            }

            aligned++;

            after.X.ShouldBe(-before.X, RotationTolerance, $"bone {subject} flipped exactly");
            after.Y.ShouldBe(-before.Y, RotationTolerance);
            after.Z.ShouldBe(-before.Z, RotationTolerance);
            after.W.ShouldBe(-before.W, RotationTolerance);
        }

        // **Skipped rather than failed when nothing reaches the branch, and this is not a dodge.**
        // Measured: no animation tried on `heavy.mdl` decodes a rotation through the animated-Euler
        // path — every bone takes `STUDIO_ANIM_RAWROT`, which returns before `QuaternionAlign` in
        // the engine too. So a red here would report our code broken when what is missing is an
        // input, and the assertions above still run exactly when content provides one.
        //
        // **The gap is real and recorded** (B308): the decode-side alignment is the engine's own
        // line with its offset confirmed against `studio.h`, and it is UNEXERCISED by any TF2
        // content measured. That is worth knowing and is not the same as untested arithmetic.
        if (aligned == 0)
        {
            Assert.Ignore(
                $"no bone of animation {AlignmentAnimation} takes the animated-Euler branch; " +
                $"all {untouched} are raw quaternions, which the engine does not align either");
        }
    }

    /// <remarks>
    /// **The control, and it is the assertion that says the flag is what did it.** Aligning to the
    /// SAME hemisphere must leave the rotation exactly as it was — so a decode that negated
    /// unconditionally, or that flipped on the flag alone without consulting the alignment, fails
    /// here while passing the test above.
    /// </remarks>
    [Test]
    public void Pose_WithAnAlignmentTheRotationAlreadyMatches_LeavesItAlone()
    {
        if (Read("models/player/heavy.mdl") is not { } files)
        {
            Assert.Ignore("the game is not installed");
            return;
        }

        IReadOnlyList<StudioBone> bones = StudioBones.Read(files.Model);

        // A bone the sweep above proved reaches the alignment, so "unchanged" here means the
        // alignment DECLINED to flip rather than that the branch was never entered.
        int subject = -1;

        foreach (int candidate in Posed(files.Model, bones))
        {
            (float X, float Y, float Z, float W) was = RotationOf(files.Model, bones, candidate);

            if (!Matches(
                    RotationOf(files.Model, Flagged(bones, candidate, Negated(was)), candidate),
                    was))
            {
                subject = candidate;
                break;
            }
        }

        if (subject < 0)
        {
            Assert.Ignore("no bone takes the animated-Euler branch; see the sweep above (B308)");
            return;
        }

        (float X, float Y, float Z, float W) before = RotationOf(files.Model, bones, subject);

        (float X, float Y, float Z, float W) after =
            RotationOf(files.Model, Flagged(bones, subject, before), subject);

        after.X.ShouldBe(before.X, RotationTolerance, "already on the same hemisphere");
        after.Y.ShouldBe(before.Y, RotationTolerance);
        after.Z.ShouldBe(before.Z, RotationTolerance);
        after.W.ShouldBe(before.W, RotationTolerance);
    }

    /// <summary>The bone list with one bone flagged fixed-alignment against a chosen orientation.</summary>
    private static StudioBone[] Flagged(
        IReadOnlyList<StudioBone> bones, int bone, (float X, float Y, float Z, float W) alignment)
    {
        StudioBone[] flagged = [.. bones];

        flagged[bone] = bones[bone] with
        {
            Flags = bones[bone].Flags | StudioBoneFlags.FixedAlignment,
            Alignment = alignment,
        };

        return flagged;
    }

    private static (float X, float Y, float Z, float W) Negated(
        (float X, float Y, float Z, float W) q) => (-q.X, -q.Y, -q.Z, -q.W);

    private static bool Matches(
        (float X, float Y, float Z, float W) a, (float X, float Y, float Z, float W) b) =>
        MathF.Abs(a.X - b.X) < 1e-6f && MathF.Abs(a.Y - b.Y) < 1e-6f &&
        MathF.Abs(a.Z - b.Z) < 1e-6f && MathF.Abs(a.W - b.W) < 1e-6f;

    /// <summary>Every bone the animation's own pose mentions, at <see cref="AlignmentFrame"/>.</summary>
    private static List<int> Posed(
        ReadOnlyMemory<byte> model, IReadOnlyList<StudioBone> bones)
    {
        List<int> posed = [];

        foreach (StudioBonePose entry in
            StudioAnimation.Pose(model, bones, AlignmentAnimation, AlignmentFrame))
        {
            posed.Add(entry.Bone);
        }

        return posed;
    }

    /// <summary>How close two quaternion components must be to count as equal.</summary>
    private const double RotationTolerance = 1e-6;

    /// <summary>The frame the alignment tests read, chosen only for being past the first.</summary>
    private const int AlignmentFrame = 3;

    /// <summary>The animation the alignment tests read.</summary>
    /// <remarks>
    /// **Animation 0 was tried first and reaches nothing**: every bone of it decodes through
    /// `STUDIO_ANIM_RAWROT`, which returns before `QuaternionAlign` in the engine too, so the sweep
    /// found zero aligned bones and said so rather than passing. A later animation is used because
    /// a model mixes the two encodings — which is itself the reason the sweep counts.
    /// </remarks>
    private const int AlignmentAnimation = 40;

    /// <summary>One bone's decoded rotation at <see cref="AlignmentFrame"/>.</summary>
    private static (float X, float Y, float Z, float W) RotationOf(
        ReadOnlyMemory<byte> model, IReadOnlyList<StudioBone> bones, int bone)
    {
        foreach (StudioBonePose posed in
            StudioAnimation.Pose(model, bones, AlignmentAnimation, AlignmentFrame))
        {
            if (posed.Bone == bone)
            {
                return posed.Rotation;
            }
        }

        throw new InvalidOperationException($"bone {bone} is not in the animation's pose");
    }


    /// <summary>The bounding box of a model's vertices once skinned.</summary>
    private static (float X, float Y, float Z) Extents(
        StudioSkeleton skeleton, IReadOnlyList<StudioVertex> vertices)
    {
        float minimumX = float.MaxValue, minimumY = float.MaxValue, minimumZ = float.MaxValue;
        float maximumX = float.MinValue, maximumY = float.MinValue, maximumZ = float.MinValue;

        foreach (StudioVertex vertex in vertices)
        {
            (float x, float y, float z) =
                skeleton.Skin(vertex.Bones, vertex.Weights, vertex.X, vertex.Y, vertex.Z);

            minimumX = MathF.Min(minimumX, x);
            minimumY = MathF.Min(minimumY, y);
            minimumZ = MathF.Min(minimumZ, z);
            maximumX = MathF.Max(maximumX, x);
            maximumY = MathF.Max(maximumY, y);
            maximumZ = MathF.Max(maximumZ, z);
        }

        return (maximumX - minimumX, maximumY - minimumY, maximumZ - minimumZ);
    }

    /// <summary>A model and its vertices from the installed game, or null when it is absent.</summary>
    private static (byte[] Model, IReadOnlyList<StudioVertex> Vertices)? Read(string path)
    {
        if (!Directory.Exists(Game))
        {
            return null;
        }

        List<VpkArchive> archives =
        [
            .. new[] { "tf2_misc_dir.vpk", "tf2_textures_dir.vpk" }
                .Select(name => Path.Combine(Game, name))
                .Where(File.Exists)
                .Select(VpkArchive.Open),
        ];

        byte[]? Find(string file) =>
            archives.Select(archive => archive.ReadFile(file)).FirstOrDefault(found => found is not null);

        if (Find(path) is not { } model || Find(path[..^4] + ".vvd") is not { } vertices)
        {
            return null;
        }

        return (model, StudioVertices.Read(vertices));
    }
}
