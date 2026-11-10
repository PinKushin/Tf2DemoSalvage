using System;
using System.Collections.Generic;

namespace Tf2DemoSalvage.Render;

/// <summary>The water reflection's world list: its own walk, from its mirrored camera, into lists of its own (B711).</summary>
/// <remarks>
/// **The one water view whose world list is its own.** Every water view builds its list in <c>CBaseWorldView::DrawSetup</c>
/// (`viewrender.cpp:5411-5440`), but through <c>g_WorldListCache</c> (`r_worldlistcache` 1, `:127`), keyed by the view's
/// camera fields and nothing else (`:302-352`) and flushed once a frame (`:2459`). The refraction, intersection and
/// under-water views copy the main view's <c>CViewSetup</c> and change at most its rectangle (`AdjustView`, `:5253-5263`),
/// so they get the main view's list — and with the cache off they would build it again from the same inputs. Only the
/// reflection's <c>AdjustView</c> moves the camera (`:5282-5284`).
///
/// **Its walk, under the main view's marks.** `R_BuildWorldLists` (engine.dll `0x1800defb0`, read in decompilation) walks
/// the tree with `0x1800e0600` from the view `DrawSetup` pushed (`:5419`), against the PVS stamps of the last
/// <c>ViewSetupVis</c>: the main view's (`SetupVis`, `:1415`), since no water view calls it. Its <c>iForceViewLeaf</c>
/// (the visible fog volume's leaf, `r_ForceWaterLeaf` 1, `:158`, `:2697-2701`) and its water height go only to
/// `0x180113f00`, the area bits, whose portal flood `0x180113b60` passes the height on to `0x180112dd0` — area portals,
/// which this project does not model (<see cref="WorldVisibility"/>). So here neither changes the list.
///
/// **A culling of its own, made by <see cref="WorldCulling.Twin"/>**, as <see cref="MonitorCull"/> is and for its reason:
/// a cull's lists are reused by the next call on the same object.
/// </remarks>
/// <param name="main">The map's culling, or null on a map without one.</param>
internal sealed class ReflectionCull(WorldCulling? main)
{
    private readonly WorldCulling? _culling = main?.Twin();
    private bool _walked;

    /// <summary>The reflection's world list from its camera, its blended runs by leaf, and its overlays.</summary>
    /// <param name="reflected">The mirrored camera (<see cref="WaterViews.Reflect"/>).</param>
    /// <param name="visOrigin">The main view's origin, which the PVS is marked from.</param>
    /// <param name="twoSided">Whether a material is <c>$nocull</c>; null for none.</param>
    /// <param name="blended">Whether a material is translucent or additive.</param>
    /// <param name="overlays">The reflection's own overlay queues, or null.</param>
    /// <returns>The lists, valid until the next call.</returns>
    public ReflectionWorld Cull(
        FreeCamera reflected, (float X, float Y, float Z) visOrigin, Func<int, bool>? twoSided, Func<int, bool> blended,
        OverlayRenderLists? overlays)
    {
        ArgumentNullException.ThrowIfNull(reflected);

        (float x, float y, float z) = reflected.Origin;
        IReadOnlyList<WorldBatch>? batches = _culling?.BatchesMarkedFrom(x, y, z, reflected.Frustum(), twoSided, visOrigin);

        _walked = batches is not null;

        (TranslucentLeafRuns? translucent, IReadOnlyList<WorldBatch> ordered) =
            WorldLists.TranslucentAndOverlays(_culling, _walked, overlays, (x, y, z), blended);

        return new ReflectionWorld(batches, translucent, ordered);
    }

    /// <summary>
    /// The faces, displacements and leaves the last walk reached — the reflection's world list, which its
    /// <c>ComputeShadowTextures</c> and <c>RenderShadows</c> read. Null surfaces and leaves when nothing was walked.
    /// </summary>
    public (IReadOnlyList<int>? Surfaces, IReadOnlyList<ReachedDisplacement>? Displacements, Func<int, bool>? LeafDrawn) Drawn =>
        _walked && _culling is { } culling ? (culling.Surfaces, culling.Displacements, culling.MainLeafTest) : (null, null, null);

    /// <summary>The last walk's leaves as the reflection's <c>PruneWorldListInfo</c> keeps them (<see cref="WorldCulling.PrunedLeaves"/>).</summary>
    /// <param name="draws">The reflection's flags.</param>
    /// <returns>The kept leaves, a delegate bound once; null for every leaf.</returns>
    public Func<int, bool>? PrunedLeaves(ViewDraws draws) => _culling?.PrunedLeaves(_walked, draws);

    /// <summary>The last walk's leaves as the reflection's <c>PruneWorldListInfo</c> lists them (<see cref="WaterViews.PruneList"/>).</summary>
    /// <param name="draws">The reflection's flags.</param>
    /// <param name="leaves">Filled with the kept leaves, nearest first; empty when nothing was walked.</param>
    /// <param name="actual">Filled with each one's index in the walk's list.</param>
    public void PruneList(ViewDraws draws, List<int> leaves, List<int> actual)
    {
        if (_walked && _culling is { } culling)
        {
            WaterViews.PruneList(culling.MainLeaves, draws, culling.LeafInWaterTest, leaves, actual);
            return;
        }

        leaves.Clear();
        actual.Clear();
    }
}

/// <summary>The water reflection's own world lists, as its draw reads them (B711).</summary>
/// <param name="Batches">The opaque runs; null to draw every batch (a map that cannot be culled).</param>
/// <param name="Translucent">The blended runs by leaf, or null.</param>
/// <param name="Overlays">The opaque world's overlays, in draw order.</param>
internal sealed record ReflectionWorld(
    IReadOnlyList<WorldBatch>? Batches, TranslucentLeafRuns? Translucent, IReadOnlyList<WorldBatch> Overlays);

/// <summary>What a view's draw needs from its world list beside the opaque runs: one place for the main view and the reflection.</summary>
internal static class WorldLists
{
    /// <summary>
    /// A walked list's blended surfaces by leaf place, with the overlays drawn after each (B426, B457), and the opaque
    /// world's overlays queued from the surfaces the same walk reached (0x1800e4fd0).
    /// </summary>
    /// <param name="culling">The culling the walk ran on, or null.</param>
    /// <param name="walked">Whether it walked, so its surfaces mean something; otherwise every face in buffer order.</param>
    /// <param name="lists">The view's overlay queues, or null.</param>
    /// <param name="eye">The view's eye.</param>
    /// <param name="blended">Whether a material is translucent or additive.</param>
    /// <returns>The blended runs, or null; and the overlays.</returns>
    public static (TranslucentLeafRuns? Translucent, IReadOnlyList<WorldBatch> Overlays) TranslucentAndOverlays(
        WorldCulling? culling, bool walked, OverlayRenderLists? lists, (float X, float Y, float Z) eye, Func<int, bool> blended) =>
        Lists(
            culling?.BlendedRuns(blended, lists is { } separate ? separate.HasOverlays : null),
            walked ? culling?.Surfaces : null,
            walked ? culling?.Displacements : null,
            culling,
            lists,
            eye,
            blended);

    /// <summary>
    /// The 3D sky view's own: its walk's blended surfaces by its leaf places and the overlays queued from its walk's surfaces —
    /// <c>CSkyboxView::DrawInternal</c>'s <c>DrawWorld</c> and translucent pass over its own world list (`viewrender.cpp:
    /// 4920-4932`, B711).
    /// </summary>
    /// <param name="culling">The culling the sky walk ran on.</param>
    /// <param name="lists">The sky view's overlay queues, or null.</param>
    /// <param name="eye">The sky view's eye.</param>
    /// <param name="blended">Whether a material is translucent or additive.</param>
    /// <returns>The blended runs, or null; and the overlays.</returns>
    public static (TranslucentLeafRuns? Translucent, IReadOnlyList<WorldBatch> Overlays) SkyTranslucentAndOverlays(
        WorldCulling culling, OverlayRenderLists? lists, (float X, float Y, float Z) eye, Func<int, bool> blended)
    {
        ArgumentNullException.ThrowIfNull(culling);

        return Lists(
            culling.SkyBlendedRuns(blended, lists is { } separate ? separate.HasOverlays : null),
            culling.SkySurfaces,
            culling.SkyDisplacements,
            culling,
            lists,
            eye,
            blended);
    }

    private static (TranslucentLeafRuns? Translucent, IReadOnlyList<WorldBatch> Overlays) Lists(
        TranslucentLeafRuns? translucent,
        IReadOnlyList<int>? surfaces,
        IReadOnlyList<ReachedDisplacement>? displacements,
        WorldCulling? culling,
        OverlayRenderLists? lists,
        (float X, float Y, float Z) eye,
        Func<int, bool> blended)
    {
        if (translucent is { } leaves && lists is { } perSurface)
        {
            leaves.OverlaysAfter = perSurface.ForTranslucentLeaves(leaves, eye);
        }

        IReadOnlyList<WorldBatch> overlays = lists?.Order(
            surfaces,
            displacements,
            eye,
            blended,
            culling is { } groups ? groups.SortGroup : null) ?? [];

        return (translucent, overlays);
    }
}
