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
