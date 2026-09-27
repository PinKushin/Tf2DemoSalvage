using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;

namespace Tf2DemoSalvage.Animation.Animating;

/// <summary>Bone-to-world folded with a model's bind pose — what the shader skins by.</summary>
/// <remarks>
/// **The one place <c>poseToBone</c> is applied**, extracted so it has exactly one implementation.
/// <c>EntityModelSet.Skinning</c> (<c>EntityModels.cs:3478</c>) is this same loop with a per-entity
/// reused buffer, because it runs once per bone per drawn prop per frame — hundreds of entities,
/// sixty times a second. A caller with nothing like that budget (a HUD model panel, drawn a handful
/// of times) calls <see cref="Fill"/> straight into a fresh array; both write identical bytes.
/// </remarks>
public static class BoneSkinning
{
    /// <summary>Fills <paramref name="destination"/> with each bone's bone-to-world × bind pose.</summary>
    /// <param name="bones">The model's rest skeleton, for <c>PoseToBone</c>.</param>
    /// <param name="accessor">This frame's posed bone-to-world matrices.</param>
    /// <param name="destination">One twelve-float array per bone, already sized to <paramref name="accessor"/>'s count.</param>
    public static void Fill(IReadOnlyList<StudioBone> bones, BoneAccessor accessor, IList<float[]> destination)
    {
        ArgumentNullException.ThrowIfNull(bones);
        ArgumentNullException.ThrowIfNull(accessor);
        ArgumentNullException.ThrowIfNull(destination);

        for (int bone = 0; bone < destination.Count; bone++)
        {
            StudioBones.Concatenate(accessor.Bone(bone), bones[bone].PoseToBone.Span, destination[bone]);
        }
    }
}
