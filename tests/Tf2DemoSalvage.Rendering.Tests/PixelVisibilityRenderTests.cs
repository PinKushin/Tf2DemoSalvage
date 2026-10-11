using System.Collections.Generic;
using System.Numerics;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The two occlusion queries `CPixelVisibilitySystem::EndView` issues per proxy, counted on a real device (B378, B521).
/// </summary>
/// <remarks>
/// **The device half of `PixelVisibilityConformanceTests`.** Those prove which quad and what the counts become; this
/// proves the counts: `IssueCountingQuery` under `DepthRange( 0.0f, 0.01f )`, which nothing hides, and `IssueQuery`
/// against the scene's depth (`c_pixel_visibility.cpp:598-625`), through `engine/occlusionproxy` — `$writez 0`.
///
/// The camera is the identity, so positions are clip space and z is depth. 64 pixels across two clip units: a proxy
/// half a unit square is 16 by 16.
/// </remarks>
public sealed class PixelVisibilityRenderTests
{
    private const int Possible = 16 * 16;

    [Test]
    public void Count_AProxyHalfBehindAnOccluder_PassesHalfItsPixels()
    {
        if (Counted(proxyDepth: 0.5f) is not var (pixels, possible))
        {
            return;
        }

        possible.ShouldBe(Possible);
        pixels.ShouldBe(Possible / 2);
    }

    /// <remarks>The control: the same proxy in front of the same wall loses nothing.</remarks>
    [Test]
    public void Count_AProxyInFrontOfTheOccluder_PassesAllItsPixels()
    {
        if (Counted(proxyDepth: 0.2f) is not var (pixels, possible))
        {
            return;
        }

        possible.ShouldBe(Possible);
        pixels.ShouldBe(Possible);
    }

    /// <remarks>`$writez 0`: a proxy leaves no depth behind, so a second one behind it still counts whole.</remarks>
    [Test]
    public void Count_AProxyBehindAnotherProxy_IsNotHiddenByIt()
    {
        if (Walled() is not { } target)
        {
            return;
        }

        using (target)
        {
            // Both on the open side of the wall, one in front of the other.
            IReadOnlyDictionary<int, (int Pixels, int Possible)> counts = target.CountOcclusion(
                [Proxy(1, 0.5f, 0.2f), Proxy(2, 0.5f, 0.5f)], Identity);

            counts[2].ShouldBe((Possible, Possible));
        }
    }

    /// <summary>One proxy centred on the wall's edge.</summary>
    private static (int Pixels, int Possible)? Counted(float proxyDepth)
    {
        if (Walled() is not { } target)
        {
            return null;
        }

        using (target)
        {
            (int Red, int Green, int Blue) before = target.PixelAt(40, 32);

            (int Pixels, int Possible) counted = target.CountOcclusion([Proxy(1, 0f, proxyDepth)], Identity)[1];

            target.PixelAt(40, 32).ShouldBe(before, "a proxy writes no colour");

            return counted;
        }
    }

    /// <summary>A target with a wall over the left half of the view at depth 0.3; `DrawWorld` is what clears the depth.</summary>
    private static OffscreenTarget? Walled()
    {
        OffscreenTarget? target = OffscreenTarget.TryCreate(64, 64);

        if (target is null)
        {
            Assert.Ignore("no Direct3D on this machine");
            return null;
        }

        if (MapCache.TryLoad("cp_process_f12", 256) is not { } assets)
        {
            target.Dispose();
            Assert.Ignore("the map or the game is not installed");
            return null;
        }

        List<WorldVertex> wall =
        [
            new(-1f, -1f, 0.3f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f),
            new(0f, 1f, 0.3f, 1f, 1f, 0f, 0f, 0f, 0f, 1f, 0f),
            new(0f, -1f, 0.3f, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f),
            new(-1f, -1f, 0.3f, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f),
            new(-1f, 1f, 0.3f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f),
            new(0f, 1f, 0.3f, 1f, 1f, 0f, 0f, 0f, 0f, 1f, 0f),
        ];

        target.Clear(0f, 0f, 0f);
        target.DrawWorld(wall, [new WorldBatch(0, 0, wall.Count)], Identity, assets, surfaceColours: true, translucent: false);

        return target;
    }

    /// <summary>A quad half a clip unit square, centred at <paramref name="x"/>.</summary>
    private static PixelVisibilityProxy Proxy(int key, float x, float depth) =>
        new(
            key,
            new Vector3(x - 0.25f, 0.25f, depth),
            new Vector3(x + 0.25f, 0.25f, depth),
            new Vector3(x + 0.25f, -0.25f, depth),
            new Vector3(x - 0.25f, -0.25f, depth));

    private static float[] Identity =>
    [
        1f, 0f, 0f, 0f,
        0f, 1f, 0f, 0f,
        0f, 0f, 1f, 0f,
        0f, 0f, 0f, 1f,
    ];
}
