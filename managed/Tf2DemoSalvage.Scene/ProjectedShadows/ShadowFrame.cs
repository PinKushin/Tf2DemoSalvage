using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>What a frame's render-to-texture shadows hand the device: the casters to draw into the atlas, and the decals.</summary>
/// <param name="Casters">Each shadow, with its cell and the entities drawn into it.</param>
/// <param name="World">The shadow decals on the world, three corners a triangle, drawn after the world's decals.</param>
/// <param name="Colour">The shadow material's <c>$color</c>, already taken from gamma to linear (<c>SetPixelShaderConstantGammaToLinear</c>).</param>
/// <param name="Brushes">The shadow decals on brush entities, placed in the world, drawn after those entities and their decals.</param>
public sealed record ShadowFrame(
    IReadOnlyList<CastShadow> Casters, IReadOnlyList<ShadowVertex> World, Vector3 Colour, IReadOnlyList<ShadowVertex> Brushes)
{
    /// <summary>A frame with no shadows.</summary>
    public static ShadowFrame None { get; } = new([], [], Vector3.One, []);

    /// <summary>
    /// `decals/simpleshadow`'s base texture, which a blob's corners sample (<c>m_SimpleShadow</c>, `clientshadowmgr.cpp:1285`);
    /// null when the install has none, and then no blob is drawn.
    /// </summary>
    public MapTexture? Blob { get; init; }
}
