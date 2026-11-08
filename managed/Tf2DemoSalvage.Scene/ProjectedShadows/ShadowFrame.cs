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
    /// The shadow decals on the surfaces the monitor view draws, cut from its own surface list — <c>RenderShadows</c> in
    /// that view's <c>R_DrawWorldLists</c> (finding 107). <see cref="World"/> is the main view's, which the water views
    /// draw too, since they draw its runs.
    /// </summary>
    public IReadOnlyList<ShadowVertex> Monitor { get; init; } = [];

    /// <summary>The shadow decals on the surfaces the 3D skybox view draws, cut from its own walk's surfaces.</summary>
    public IReadOnlyList<ShadowVertex> Sky { get; init; } = [];

    /// <summary>
    /// `decals/simpleshadow`'s base texture, which a blob's corners sample (<c>m_SimpleShadow</c>, `clientshadowmgr.cpp:1285`);
    /// null when the install has none, and then no blob is drawn.
    /// </summary>
    public MapTexture? Blob { get; init; }

    /// <summary>
    /// A frame replacing one no draw took: each of its shadows on the same cell as one the replaced frame was to redraw is
    /// redrawn too, with that one's hierarchy when it has none of its own — the dirty flag the engine clears only once the
    /// cell is drawn (`DrawRenderToTextureShadow`, `clientshadowmgr.cpp:3774-3796`).
    /// </summary>
    /// <param name="undrawn">The frame no draw took.</param>
    /// <param name="next">The frame replacing it.</param>
    /// <returns><paramref name="next"/> with the owed redraws.</returns>
    /// <exception cref="System.ArgumentNullException">An argument is null.</exception>
    public static ShadowFrame CarryRedraws(ShadowFrame undrawn, ShadowFrame next)
    {
        System.ArgumentNullException.ThrowIfNull(undrawn);
        System.ArgumentNullException.ThrowIfNull(next);

        Dictionary<(int Entity, ShadowCell Cell), IReadOnlyList<int>> owed = [];

        foreach (CastShadow shadow in undrawn.Casters)
        {
            if (shadow.Redraw)
            {
                owed[(shadow.EntityIndex, shadow.Cell)] = shadow.Hierarchy;
            }
        }

        if (owed.Count == 0)
        {
            return next;
        }

        List<CastShadow> casters = new(next.Casters.Count);

        foreach (CastShadow shadow in next.Casters)
        {
            if (shadow.Redraw || shadow.Blob || !owed.TryGetValue((shadow.EntityIndex, shadow.Cell), out IReadOnlyList<int>? hierarchy))
            {
                casters.Add(shadow);
                continue;
            }

            casters.Add(shadow with { Redraw = true, Hierarchy = shadow.Hierarchy.Count > 0 ? shadow.Hierarchy : hierarchy });
        }

        return next with { Casters = casters };
    }
}
