using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>What a frame's render-to-texture shadows hand the device: the casters to draw into the atlas, and the world's decals.</summary>
/// <param name="Casters">Each shadow, with its cell and the entities drawn into it.</param>
/// <param name="World">The shadow decals on the world, three corners a triangle.</param>
/// <param name="Colour">The shadow material's <c>$color</c>, already taken from gamma to linear (<c>SetPixelShaderConstantGammaToLinear</c>).</param>
public sealed record ShadowFrame(IReadOnlyList<CastShadow> Casters, IReadOnlyList<ShadowVertex> World, Vector3 Colour)
{
    /// <summary>A frame with no shadows.</summary>
    public static ShadowFrame None { get; } = new([], [], Vector3.One);
}
