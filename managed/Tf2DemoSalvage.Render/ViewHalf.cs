using System.Collections.Generic;

namespace Tf2DemoSalvage.Render;

/// <summary>One half of the view state `SetCamera` hands the draw: its own culling, its own runs, and what the cull decided
/// beside them (B581 rung 2, D228).</summary>
/// <remarks>
/// **Two of these, used alternately, so the next frame's cull can run while the previous frame draws** — the engine's
/// queued context records into one frame while the render thread plays the other (finding 99). Each half owns a
/// <see cref="WorldCulling"/> and an <see cref="OverlayRenderLists"/> of its own, because both reuse the lists they
/// return: through one object, a cull for frame N+1 would rewrite frame N's runs under its draw.
///
/// **Each half remembers the view it culled for**, so a still camera still pays nothing: each half culls once, then both
/// skip. **Not here yet**: the sky decision, the water plan (`_waterDraw` reads `_eye` while it draws), the frustums, the
/// camera upload and the detail sprites, which the device still keeps once. They stay right because a half skips only
/// after a frame of the same view (`Device3D.SetCamera`); moving the drain later needs them here (finding 98 §9).
/// </remarks>
internal sealed class ViewHalf
{
    /// <summary>This half's culling of the map, or null on a map without one.</summary>
    public WorldCulling? Culling { get; set; }

    /// <summary>This half's overlay queues.</summary>
    public OverlayRenderLists? OverlayLists { get; set; }

    /// <summary>The view this half's runs were culled for, or null to cull on its next camera.</summary>
    public ((float X, float Y, float Z) Origin, (float Pitch, float Yaw, float Roll) Angles,
            float Fov, float Near, float Far, float Aspect)? CulledFor { get; set; }

    /// <summary>The world's visible runs; null for "draw every batch".</summary>
    public IReadOnlyList<WorldBatch>? VisibleBatches { get; set; }

    /// <summary>The blended surfaces by leaf place.</summary>
    public TranslucentLeafRuns? TranslucentLeaves { get; set; }

    /// <summary>The opaque world's overlays, in draw order.</summary>
    public IReadOnlyList<WorldBatch>? Overlays { get; set; }

    /// <summary>The 3D skybox room's runs.</summary>
    public IReadOnlyList<WorldBatch> SkyBatches { get; set; } = [];

    /// <summary>Hands this half's runs to the world renderer, which draws from them.</summary>
    /// <param name="world">The renderer.</param>
    public void InstallInto(WorldRenderer world)
    {
        world.VisibleBatches = VisibleBatches;
        world.TranslucentLeaves = TranslucentLeaves;
        world.Overlays = Overlays;
        world.SkyBatches = SkyBatches;
    }
}
