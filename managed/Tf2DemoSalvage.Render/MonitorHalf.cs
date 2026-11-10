using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// One point camera's monitor view in one <see cref="ViewHalf"/>: its camera, its cull and lists, its water plan and its
/// detail build (B511, B711, B715).
/// </summary>
/// <remarks>
/// **One per camera <c>DrawMonitors</c> draws** (`viewrender.cpp:3240-3293`): every active, non-dormant camera renders a whole
/// view of its own, so each keeps its own world list and the lists made from it. Kept by place in the walk and reused
/// across frames, so a still camera at the same place culls nothing again.
/// </remarks>
/// <param name="culling">The map's culling, which <see cref="Cull"/> and its reflection's cull twin; null on a map without one.</param>
/// <param name="overlays">Its own overlay queues, since a queue reuses the lists it returns; null before a world.</param>
/// <param name="reflectionOverlays">Its water reflection's own, likewise.</param>
internal sealed class MonitorHalf(WorldCulling? culling, OverlayRenderLists? overlays, OverlayRenderLists? reflectionOverlays)
{
    /// <summary>The point camera this view draws from this frame.</summary>
    public ScenePointCamera Camera { get; set; }

    /// <summary>Its view, built on the main view's setup.</summary>
    public FreeCamera? View { get; set; }

    /// <summary>Its cull, a twin of the map's: its lists are read by this half's draw and shadows alone.</summary>
    public MonitorCull Cull { get; } = new(culling);

    /// <summary>The world runs its camera sees, culled on the game thread with the main view (B702); null to draw all.</summary>
    public IReadOnlyList<WorldBatch>? Batches { get; set; }

    /// <summary>Its own overlay queues (B711).</summary>
    public OverlayRenderLists? OverlayLists { get; } = overlays;

    /// <summary>Its blended runs by leaf, its overlays, and the cull version they were made from (B711).</summary>
    public (TranslucentLeafRuns? Translucent, IReadOnlyList<WorldBatch> Overlays, int Version)? Lists { get; set; }

    /// <summary>Its own water plan, from its camera, drawn before its world view (B711, `viewrender.cpp:3168-3238`).</summary>
    public WaterPlan Water { get; } = new()
    {
        Monitor = true, ReflectionOverlayLists = reflectionOverlays, ReflectionCull = new ReflectionCull(culling),
    };

    /// <summary>What its water plan was made from, or null for none made (B711).</summary>
    public (int Version, bool Skybox, float CheapEnd, float Aspect, float Near, float Far)? WaterFor { get; set; }

    /// <summary>How many of the half's collated water views are its own (<see cref="WaterPlan.Collate"/>).</summary>
    public int WaterViews { get; set; }

    /// <summary>Its detail build, around its point camera (B711).</summary>
    public DetailBuild Detail { get; } = new();

    /// <summary>Whether the scene collates its view this frame: it drew, and its cull listed its leaves (B711).</summary>
    public bool Collated => Batches is not null && View is not null && Cull.Leaves is not null;
}
