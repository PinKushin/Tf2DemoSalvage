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

    /// <remarks>
    /// **Every proxy counts its OWN quad** when one upload serves them all and each query draws its six corners by offset
    /// (B597): hidden, half hidden, open, and in front of the wall, issued together.
    /// </remarks>
    [Test]
    public void Count_SeveralProxiesIssuedTogether_EachCountsItsOwnQuad()
    {
        if (Walled() is not { } target)
        {
            return;
        }

        using (target)
        {
            IReadOnlyDictionary<int, (int Pixels, int Possible)> counts = target.CountOcclusion(
                [Proxy(1, -0.5f, 0.5f), Proxy(2, 0f, 0.5f), Proxy(3, 0.5f, 0.5f), Proxy(4, -0.5f, 0.2f)], Identity);

            counts[1].ShouldBe((0, Possible), "behind the wall");
            counts[2].ShouldBe((Possible / 2, Possible), "on its edge");
            counts[3].ShouldBe((Possible, Possible), "clear of it");
            counts[4].ShouldBe((Possible, Possible), "in front of it");
        }
    }

    /// <remarks>
    /// **The shared upload across a buffer's growth and its reuse** (B597): a frame of 4 proxies (24 corners, the line
    /// renderer's first 128-vertex buffer), then 32 (192 corners, so the buffer is rebuilt at 256), then 32 again with every
    /// depth swapped (the 256 reused). Every proxy over the wall's half alternates in front of it and behind it, so a query
    /// drawing another proxy's corners, or corners left from the frame before, counts the wrong one.
    /// </remarks>
    [Test]
    public void Count_AFrameThatGrowsTheBufferThenOneThatReusesIt_CountsEveryProxyAsItsOwn()
    {
        if (Walled() is not { } target)
        {
            return;
        }

        using (target)
        {
            // Over the wall (x < 0), 4 columns by 8 rows; a proxy at 0.2 is in front of it, at 0.5 behind it.
            List<PixelVisibilityProxy> Grid(int count, bool evenInFront)
            {
                List<PixelVisibilityProxy> grid = [];

                for (int cell = 0; cell < count; cell++)
                {
                    float x = -0.875f + (cell % 4 * 0.25f);
                    float y = -0.875f + (cell / 4 * 0.25f);
                    bool front = cell % 2 == 0 == evenInFront;
                    float depth = front ? 0.2f : 0.5f;

                    grid.Add(new PixelVisibilityProxy(
                        cell + 1,
                        new Vector3(x - 0.08f, y + 0.08f, depth),
                        new Vector3(x + 0.08f, y + 0.08f, depth),
                        new Vector3(x + 0.08f, y - 0.08f, depth),
                        new Vector3(x - 0.08f, y - 0.08f, depth)));
                }

                return grid;
            }

            void Expect(IReadOnlyDictionary<int, (int Pixels, int Possible)> counts, int count, bool evenInFront, string frame)
            {
                for (int cell = 0; cell < count; cell++)
                {
                    (int pixels, int possible) = counts[cell + 1];
                    bool front = cell % 2 == 0 == evenInFront;

                    possible.ShouldBeGreaterThan(0, $"{frame}: proxy {cell + 1} is on screen");
                    pixels.ShouldBe(front ? possible : 0, $"{frame}: proxy {cell + 1} is {(front ? "in front of" : "behind")} the wall");
                }
            }

            Expect(target.CountOcclusion(Grid(4, evenInFront: true), Identity), 4, evenInFront: true, "the small frame");
            Expect(target.CountOcclusion(Grid(32, evenInFront: true), Identity), 32, evenInFront: true, "the frame that grows it");
            Expect(target.CountOcclusion(Grid(32, evenInFront: false), Identity), 32, evenInFront: false, "the frame that reuses it");
        }
    }

    /// <remarks>
    /// **The counting query is squeezed, not untested**: <c>pRenderContext->DepthRange( 0.0f, 0.01f )</c> around
    /// `IssueCountingQuery` (`c_pixel_visibility.cpp:598-611`), with the depth test still on. A proxy at depth 0.5
    /// counts at 0.005, so a wall nearer than that hides the COUNT as well, and the ratio is of what is left.
    /// </remarks>
    [Test]
    public void Count_AnOccluderNearerThanTheSqueezedRange_HidesTheCountingQueryToo()
    {
        if (Counted(proxyDepth: 0.5f, wallDepth: 0.001f) is not var (pixels, possible))
        {
            return;
        }

        possible.ShouldBe(Possible / 2);
        pixels.ShouldBe(Possible / 2);
    }

    /// <summary>One proxy centred on the wall's edge.</summary>
    private static (int Pixels, int Possible)? Counted(float proxyDepth, float wallDepth = 0.3f)
    {
        if (Walled(wallDepth) is not { } target)
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

    /// <summary>A target with a wall over the left half of the view, at depth 0.3 unless told; `DrawWorld` is what clears the depth.</summary>
    private static OffscreenTarget? Walled(float depth = 0.3f)
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
            new(-1f, -1f, depth, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f),
            new(0f, 1f, depth, 1f, 1f, 0f, 0f, 0f, 0f, 1f, 0f),
            new(0f, -1f, depth, 1f, 0f, 0f, 0f, 0f, 0f, 1f, 0f),
            new(-1f, -1f, depth, 0f, 0f, 0f, 0f, 0f, 0f, 1f, 0f),
            new(-1f, 1f, depth, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f),
            new(0f, 1f, depth, 1f, 1f, 0f, 0f, 0f, 0f, 1f, 0f),
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
