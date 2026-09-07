using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Tests;

/// <summary>
/// The two-bone skeletons a ragdoll's construction is predicted against (B58).
/// </summary>
/// <remarks>
/// **Synthetic, because a synthetic fixture HAS ground truth** (D38): the bind pose here is one
/// this file chose, so every offset and every axis below is an exact prediction rather than a
/// second reading of a real model.
///
/// **The bind positions are asymmetric and non-zero in two axes on purpose.** A pair of bones a
/// unit apart along one axis gives the same answer whichever way round a transcription reads them;
/// `(1, 2, 3)` and `(4, 6, 3)` give `(3, 4, 0)` one way and `(−3, −4, 0)` the other.
/// </remarks>
internal static class RagdollSkeletons
{
    /// <summary>Two unrotated bones — the fixture every offset prediction is taken from.</summary>
    /// <remarks>
    /// **`poseToBone` is the WORLD-to-bone matrix**, so a bone standing at `p` with no rotation
    /// carries a translation of `−p`.
    /// </remarks>
    public static IReadOnlyList<StudioBone> Straight() =>
        [
            Bone("bip_root", -1, 1f, 2f, 3f),
            Bone("bip_child", 0, 4f, 6f, 3f),
        ];

    /// <summary>The same two bones, with the child turned a quarter turn about Z.</summary>
    /// <remarks>
    /// **This fixture exists because an unrotated pair cannot tell a kept rotation from a discarded
    /// one** — both predict the identity, which is the condition where correct and broken produce
    /// the same observation. Turned, the child's own X axis points along the parent's `+Y` and its
    /// Y axis along the parent's `−X`.
    ///
    /// **A turned bone's `poseToBone` is the TRANSPOSE with a rotated translation**, because
    /// `inverse(R | t)` is `(Rᵀ | −Rᵀt)`: standing at `(4, 6, 3)` turned +90° about Z gives
    /// `(−6, 4, −3)`. Writing plain `−t` there describes a skeleton that does not exist, and every
    /// prediction taken from it would be wrong while still looking orthonormal.
    ///
    /// **The bind OFFSET is unchanged at `(3, 4, 0)`**: turning a bone in place moves its axes and
    /// not its origin, so the rotation can be asserted without disturbing what the older tests
    /// predict.
    /// </remarks>
    public static IReadOnlyList<StudioBone> Turned() =>
        [
            Bone("bip_root", -1, 1f, 2f, 3f),
            new StudioBone(
                "bip_child",
                0,
                (4f, 6f, 3f),
                (0f, 0f, SinOf45, SinOf45),
                new float[]
                {
                    0f, 1f, 0f, -6f,
                    -1f, 0f, 0f, 4f,
                    0f, 0f, 1f, -3f,
                }),
        ];

    /// <summary>sin 45° — and cos 45°, which is why a quarter-turn quaternion repeats it.</summary>
    public const float SinOf45 = 0.70710678f;

    /// <summary>An unrotated bone at a chosen bind position, with the matrix that implies.</summary>
    private static StudioBone Bone(string name, int parent, float x, float y, float z) =>
        new(
            name,
            parent,
            (x, y, z),
            (0f, 0f, 0f, 1f),
            new float[]
            {
                1f, 0f, 0f, -x,
                0f, 1f, 0f, -y,
                0f, 0f, 1f, -z,
            });
}
