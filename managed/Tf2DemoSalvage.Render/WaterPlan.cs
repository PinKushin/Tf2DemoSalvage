using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Render;

/// <summary>
/// One camera's <c>DrawWorldAndEntities</c> water decision and the lists its water views draw from (B62, B711): the main view's,
/// and the monitor's, which builds its own water views from its own camera (`ViewDrawScene( …, VIEW_MONITOR )`,
/// `viewrender.cpp:3168-3238`, `:2649-2718`).
/// </summary>
/// <remarks>
/// **One of these per camera, because nothing in the engine's water decision asks which view it is**:
/// <c>DetermineWaterRenderInfo</c> and <c>DrawWorldAndEntities</c> carry no view-id branch (`:2485-2718`), so the monitor's
/// reflection and refraction are the main view's views from another eye, drawn into the same global targets first. "Own view"
/// below is the view the water views precede — the main view in the main plan, the monitor's world view in the monitor's.
/// </remarks>
internal sealed class WaterPlan
{
    /// <summary>The water views, or null when the camera's view is one simple view (B62).</summary>
    public WaterDraw? WaterDraw { get; set; }

    /// <summary>The water plan last logged for this camera (volume, leaf, eye in, material, cheap end, info), so the line fires on a change.</summary>
    public (int Volume, int Leaf, bool EyeIn, int Material, float CheapEnd, WaterRenderInfo Info)? LastWaterPlan { get; set; }

    /// <summary>The visible volume's own fog the own view draws under from inside it.</summary>
    public Tf2DemoSalvage.Core.Scene.SceneFog? WaterMainFog { get; set; }

    /// <summary>
    /// The colour the own view clears to after the 3D sky, the value an sRGB view needs: only the eye-in-volume simple
    /// view, <c>VIEW_CLEAR_COLOR</c> to <c>GetFogColor</c> unscaled (<c>viewrender.cpp:5772-5780</c>). Null for no clear.
    /// </summary>
    public (float Red, float Green, float Blue)? WaterMainClear { get; set; }

    /// <summary>The view's under-water overlay, <see cref="WaterViews.UnderwaterOverlay"/>, or null (B658).</summary>
    public ScreenOverlayMaterial? UnderwaterOverlay { get; set; }

    /// <summary>The own view's draw flags when it is a <c>CSimpleWorldView</c>.</summary>
    public ViewDraws SimpleViewDraws { get; set; } = WorldRenderer.AllWaterDraws;

    /// <summary>
    /// The water views drawn before the own one, as their shadows read them, in draw order (B702, B711): whether it is the
    /// reflection, whose surfaces are its own cull's, and the leaves its pruned world list keeps. Empty for a simple view.
    /// </summary>
    public List<(bool Reflection, Func<int, bool>? Leaves)> WaterShadowViews { get; } = [];

    /// <summary>The own view's world-list leaves as its <c>PruneWorldListInfo</c> keeps them, for its shadows (B711); null for every leaf.</summary>
    public Func<int, bool>? MainShadowLeaves { get; set; }

    /// <summary>
    /// The own view's pruned list when it draws one water side alone (B711, D251): its leaves, which its renderables
    /// collate over, and each one's <c>m_pActualLeafIndex</c> in the world list. Null when it draws both sides, the list as it is.
    /// </summary>
    public (List<int> Leaves, List<int> Actual)? MainPruned { get; set; }

    /// <summary>The own view's pruned list's storage, reused frame to frame.</summary>
    public (List<int> Leaves, List<int> Actual) MainPrunedStorage { get; } = ([], []);

    /// <summary>An own-view leaf place as its place in the world list: <c>m_pActualLeafIndex</c>, or itself unpruned.</summary>
    /// <param name="place">The place in the own view's (pruned) list.</param>
    /// <returns>The world-list place; a place past the pruned list is returned as it is.</returns>
    public int MainActual(int place) =>
        MainPruned is { } pruned && place >= 0 && place < pruned.Actual.Count ? pruned.Actual[place] : place;

    /// <summary>
    /// The water views that draw entities, in draw order, as the scene collates their renderables (B711): each one's pruned
    /// leaves and the frustum its <c>DrawSetup</c> pushes.
    /// </summary>
    public List<RenderableView> WaterRenderables { get; } = [];

    /// <summary>
    /// Beside <see cref="WaterRenderables"/>: the view, the origin and forward its translucent entries sort by, and its
    /// camera matrix, which its particles draw through.
    /// </summary>
    public List<(WaterView View, (float X, float Y, float Z) Eye, (float X, float Y, float Z) Forward, float[] Matrix)> WaterRenderers { get; } = [];

    /// <summary>Each water view's pruned leaves and their indices in its unpruned list, reused frame to frame.</summary>
    public List<(List<int> Leaves, List<int> Actual)> WaterLeafLists { get; } = [];

    /// <summary>
    /// Every water view drawn before the own one, with its pruned list's <c>m_pActualLeafIndex</c>: the leaves of its world
    /// list its translucent pass walks, back to front (B711).
    /// </summary>
    public List<(WaterView View, List<int> Actual)> WaterPruned { get; } = [];

    /// <summary>
    /// A water view's <c>m_pActualLeafIndex</c> from this plan, or null for one the plan does not hold: the places its
    /// translucent pass interleaves by, into the world list its draw installs (the camera's own, or the reflection's).
    /// </summary>
    /// <param name="view">The water view.</param>
    /// <returns>Its pruned list's indices into the world list, or null.</returns>
    public List<int>? PrunedActual(WaterView view)
    {
        int at = WaterPruned.FindIndex(each => each.View == view);

        return at >= 0 ? WaterPruned[at].Actual : null;
    }

    /// <summary>
    /// This plan's first place among the water views the scene collated this frame, which its views' entries are found from;
    /// null when the scene collated none of them (B711).
    /// </summary>
    public int? CollatedFrom { get; private set; }

    /// <summary>
    /// The water views the scene collates this frame, in draw order (B711): the monitor's own first, as <c>DrawOneMonitor</c>
    /// runs before the scene (`viewrender.cpp:2074-2081`) and its <c>DrawWorldAndEntities</c> draws them ahead of its world
    /// view, then the main view's. Records where each plan's begin.
    /// </summary>
    /// <param name="into">The list to fill, reused frame to frame.</param>
    /// <param name="monitor">The monitor's plan.</param>
    /// <param name="monitorDraws">Whether the monitor draws this frame; without it, its views are not collated.</param>
    /// <param name="main">The main view's plan.</param>
    /// <returns>How many of the collated views, from the first, are the monitor's.</returns>
    public static int Collate(List<RenderableView> into, WaterPlan monitor, bool monitorDraws, WaterPlan main)
    {
        ArgumentNullException.ThrowIfNull(into);
        ArgumentNullException.ThrowIfNull(monitor);
        ArgumentNullException.ThrowIfNull(main);

        into.Clear();
        monitor.CollatedFrom = monitorDraws ? 0 : null;

        if (monitorDraws)
        {
            into.AddRange(monitor.WaterRenderables);
        }

        int monitors = into.Count;

        main.CollatedFrom = monitors;
        into.AddRange(main.WaterRenderables);

        return monitors;
    }

    /// <summary>Whether this is the monitor's plan, whose water views but the reflection draw the monitor's world list (B711).</summary>
    public bool Monitor { get; init; }

    /// <summary>This camera's water reflection cull, a twin of its own (B711).</summary>
    public ReflectionCull ReflectionCull { get; set; } = new(null);

    /// <summary>The reflection's own overlay queues, since a queue reuses the lists it returns (B711).</summary>
    public OverlayRenderLists? ReflectionOverlayLists { get; set; }

    /// <summary>
    /// The reflection's own eye and forward when it draws entities, which its detail sprites are built from
    /// (`UpdateRenderablesOpacity` → `BuildDetailObjectRenderLists( origin )`, `viewrender.cpp:3470-3491`, B711); null otherwise.
    /// </summary>
    public ((float X, float Y, float Z) Eye, (float X, float Y, float Z) Forward)? ReflectionDetailEye { get; set; }

    /// <summary>The reflection's detail sprite quads, built from its mirrored eye.</summary>
    public List<DetailSpriteVertex> ReflectionDetailCorners { get; } = [];

    /// <summary>The reflection's quads by BSP leaf, with its translucent pass's cursor.</summary>
    public DetailSpriteLeaves ReflectionDetailLeaves { get; } = new();

    /// <summary>What the reflection's sprites were last built for.</summary>
    public (float X, float Y, float Z, float Distance, float Fade, float Factor, (float, float, float) Forward)? ReflectionDetailBuiltFor { get; set; }

    /// <summary>Forgets the reflection's detail sprites, for a new map's props.</summary>
    public void ClearDetail()
    {
        ReflectionDetailCorners.Clear();
        ReflectionDetailLeaves.Clear();
        ReflectionDetailBuiltFor = null;
    }

    /// <summary>Forgets the water plan, for a new map's materials or a frame that plans none.</summary>
    public void Clear()
    {
        WaterDraw = null;
        CollatedFrom = null;
        WaterShadowViews.Clear();
        WaterRenderables.Clear();
        WaterRenderers.Clear();
        WaterPruned.Clear();
        ReflectionDetailEye = null;
        MainShadowLeaves = null;
        MainPruned = null;
        WaterMainFog = null;
        WaterMainClear = null;
        UnderwaterOverlay = null;
        SimpleViewDraws = WorldRenderer.AllWaterDraws;
    }
}
