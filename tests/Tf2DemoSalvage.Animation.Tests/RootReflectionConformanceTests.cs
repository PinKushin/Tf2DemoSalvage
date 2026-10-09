using System;

using Tf2DemoSalvage.Animation.Animating;
using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>`ApplyBoneMatrixTransform` on ROOT bones only — how a left-handed viewmodel is mirrored (B515).</summary>
/// <remarks>
/// `C_BaseAnimating::BuildTransformations` (`c_baseanimating.cpp:1599-1603`), after the bone is built:
///
/// <code>
/// if (hdr-&gt;boneParent(i) == -1)
/// {
///     // Apply client-side effects to the transformation matrix
///     ApplyBoneMatrixTransform( GetBoneForWrite( i ) );
/// }
/// </code>
///
/// So the root takes the transform and a child takes it by concatenating onto that root — a child
/// transformed again would be mirrored twice, which is no mirror at all. `C_BaseViewModel`'s
/// override is the reflection (`c_baseviewmodel.cpp:239`).
/// </remarks>
public sealed class RootReflectionConformanceTests
{
    private const int Everything = StudioBoneFlags.UsedByAnything;

    /// <summary>A mirror through the plane y = 0, row-major 3x4.</summary>
    private static readonly float[] MirrorY = [1f, 0f, 0f, 0f, 0f, -1f, 0f, 0f, 0f, 0f, 1f, 0f];

    [Test]
    public void Build_WithARootReflection_MirrorsTheRootAndTheChildInheritsIt()
    {
        // Root at (10, 4, 0), child 3 further along +Y. Mirrored through y = 0: root at (10, -4, 0),
        // child at (10, -7, 0) — once, through its parent. Transformed twice it would sit at +7.
        AnimatingEntity entity = Entity(reflection: MirrorY);

        entity.SetupBones(Everything, 0d).ShouldBeTrue();

        entity.Bones.Bone(0)[7].ShouldBe(-4f, 1e-5f);
        entity.Bones.Bone(1)[7].ShouldBe(-7f, 1e-5f);
        entity.Bones.Bone(1)[3].ShouldBe(10f, 1e-5f);
    }

    [Test]
    public void Build_WithoutOne_LeavesTheSkeletonAsBuilt()
    {
        // The control: the same skeleton with no reflection is where the bind pose says.
        AnimatingEntity entity = Entity(reflection: null);

        entity.SetupBones(Everything, 0d).ShouldBeTrue();

        entity.Bones.Bone(0)[7].ShouldBe(4f, 1e-5f);
        entity.Bones.Bone(1)[7].ShouldBe(7f, 1e-5f);
    }

    private static AnimatingEntity Entity(float[]? reflection)
    {
        StudioBone[] bones =
        [
            new("root", -1, (10f, 4f, 0f), (0f, 0f, 0f, 1f), ReadOnlyMemory<float>.Empty, Flags: Everything),
            new("child", 0, (0f, 3f, 0f), (0f, 0f, 0f, 1f), ReadOnlyMemory<float>.Empty, Flags: Everything),
        ];

        SkeletonPose pose = new(bones, (_, _, _, _) => []) { RootReflection = reflection };

        return new AnimatingEntity(pose, new BoneFrameCounter());
    }
}
