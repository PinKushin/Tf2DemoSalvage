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
    /// The shadow decals on the surfaces each monitor view draws, in <c>DrawMonitors</c>' order, cut from its own surface
    /// list — <c>RenderShadows</c> in that view's <c>R_DrawWorldLists</c> (finding 107, B715). <see cref="World"/> is the main
    /// view's, which the water views other than the reflection draw too, since their world list is the main view's (B711).
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ShadowVertex>> Monitors { get; init; } = [];

    /// <summary>The shadow decals on the surfaces the water reflection draws, cut from its own world list (B711).</summary>
    public IReadOnlyList<ShadowVertex> Reflection { get; init; } = [];

    /// <summary>
    /// The shadow decals on the surfaces each monitor camera's reflection draws, in the monitors' order, cut from its own world
    /// list (B711, B715); empty for a camera whose view draws no reflection.
    /// </summary>
    public IReadOnlyList<IReadOnlyList<ShadowVertex>> MonitorReflections { get; init; } = [];

    /// <summary>The shadow decals on the surfaces the 3D skybox view draws, cut from its own walk's surfaces.</summary>
    public IReadOnlyList<ShadowVertex> Sky { get; init; } = [];

    /// <summary>
    /// `decals/simpleshadow`'s base texture, which a blob's corners sample (<c>m_SimpleShadow</c>, `clientshadowmgr.cpp:1285`);
    /// null when the install has none, and then no blob is drawn.
    /// </summary>
    public MapTexture? Blob { get; init; }
}
