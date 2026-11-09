using System.Collections.Generic;

namespace Tf2DemoSalvage.Scene;

/// <summary>What the glow occlusion gate asks of the device that counts pixels for it (B378, B521, D214).</summary>
/// <remarks>
/// `PixelVisibility` decides how bright each glow is from how many pixels of its proxy the last frame's depth let
/// through. Counting them is the GPU's; which proxies to count, and what to do with the counts, is not. **Per view**
/// (B711): each view issues its own proxies against its own depth, and its queries answer for it alone.
/// </remarks>
public interface IGlowOcclusion
{
    /// <summary>How many pixels of an entity's proxy the last frame drew in a view, of how many it could have.</summary>
    /// <param name="entity">The entity the proxy belongs to.</param>
    /// <param name="view">The view that issued it; 0 is the main view.</param>
    /// <returns>The counts, or null when no result is ready.</returns>
    public (int Pixels, int Possible)? OcclusionResult(int entity, int view = 0);

    /// <summary>The proxies a view counts at the end of its draw this frame, read back by the next.</summary>
    /// <param name="proxies">The proxies.</param>
    /// <param name="view">The view; 0 is the main view.</param>
    public void SetOcclusionProxies(IReadOnlyList<PixelVisibilityProxy> proxies, int view = 0);
}
