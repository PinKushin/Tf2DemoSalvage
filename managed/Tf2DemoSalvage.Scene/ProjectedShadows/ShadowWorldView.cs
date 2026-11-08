using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.ProjectedShadows;

/// <summary>Which world view a <see cref="ShadowWorldView"/> is, and so which decal list it draws.</summary>
public enum ShadowViewKind
{
    /// <summary>A monitor's <c>ViewDrawScene</c> (`DrawMonitors`, `viewrender.cpp:3240-3287`), with its own surfaces.</summary>
    Monitor,

    /// <summary>The 3D skybox's <c>CSkyboxView</c> (`viewrender.cpp:4869-4957`), with its own surfaces.</summary>
    Sky,

    /// <summary>A water reflection or refraction view, which here draws the main view's surfaces and so its decals.</summary>
    Water,

    /// <summary>The main view.</summary>
    Main,
}

/// <summary>
/// One world view of the frame as the shadows read it (B702): what its <c>ComputeShadowTextures</c> enumerates by
/// (`viewrender.cpp:5494`, `:4924`), and the surfaces its <c>R_DrawWorldLists</c> hands <c>RenderShadows</c>.
/// </summary>
/// <param name="Kind">Which view.</param>
/// <param name="Frustum">Its volume.</param>
/// <param name="Screen">Its screen, for each shadow's area.</param>
/// <param name="LeafDrawn">Whether its world list holds a BSP leaf; null for a map that cannot be culled, every leaf.</param>
/// <param name="Surfaces">The world faces it draws, in draw order; null for every face. A water view's are the main view's.</param>
/// <param name="Displacements">The displacements its walk reached.</param>
public readonly record struct ShadowWorldView(
    ShadowViewKind Kind,
    ViewFrustum Frustum,
    ScreenFadeView Screen,
    Func<int, bool>? LeafDrawn,
    IReadOnlyList<int>? Surfaces,
    IReadOnlyList<ReachedDisplacement>? Displacements);
