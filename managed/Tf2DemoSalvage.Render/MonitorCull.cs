using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Render;

/// <summary>The monitor view's world cull: from its own camera, into lists of its own (B511, finding 98 §9 step 0).</summary>
/// <remarks>
/// **A culling of its own, made by <see cref="WorldCulling.Twin"/>**, because every list a cull returns is reused by the
/// next call on the same object. Culled through the main view's culling, the monitor's runs were refilled by the next
/// main cull, so the monitor drew the main view's runs from its second frame on; and its own first cull refilled the
/// main view's list and its visible-leaf set, under that frame's main draw. The engine builds the monitor's visible set
/// from its own camera (`ViewDrawScene` from `DrawMonitors`, `viewrender.cpp:3240-3287`).
///
/// **Kept while the camera does not move**, as the cache it replaces was: the answer is a function of the camera.
/// </remarks>
/// <param name="main">The map's culling, or null on a map without one.</param>
internal sealed class MonitorCull(WorldCulling? main)
{
    private readonly WorldCulling? _culling = main?.Twin();
    private ScenePointCamera? _for;
    private IReadOnlyList<WorldBatch>? _batches;

    /// <summary>The world runs the monitor's camera sees, culled again only when that camera changed.</summary>
    /// <param name="camera">The monitor's camera, the cache key.</param>
    /// <param name="x">Its view origin.</param>
    /// <param name="y">Its view origin.</param>
    /// <param name="z">Its view origin.</param>
    /// <param name="frustum">Its view volume.</param>
    /// <param name="twoSided">Whether a material is <c>$nocull</c>; null for none.</param>
    /// <returns>The runs, or null to draw every batch (no culling).</returns>
    public IReadOnlyList<WorldBatch>? Batches(
        ScenePointCamera camera, float x, float y, float z, ViewFrustum frustum, Func<int, bool>? twoSided = null)
    {
        if (_for != camera)
        {
            _for = camera;
            _batches = _culling?.Batches(x, y, z, frustum, twoSided);
        }

        return _batches;
    }
}
