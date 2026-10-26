using System;
using System.Collections.Generic;

using Tf2DemoSalvage.Content.Bsp;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>What building the detail sprites costs per frame, apart from what it builds (the conformance suite holds that).</summary>
public sealed class DetailSpritesTests
{
    private static readonly BspDetailSprite Rectangle = new((-8f, 20f), (8f, 0f), (0f, 0f), (0.5f, 0.5f));

    /// <remarks>
    /// B599: <see cref="DetailSprites.Build"/> runs every frame from <c>Device3D.SetCamera</c>, and made two lists and a
    /// capturing sort comparison each time — ~0.1 MB a frame on f12 (measurements/2026-10-25-allocation-attribution.md).
    /// The second build of the same view, into a list already big enough, must allocate nothing. The control is that
    /// it built the same quads, both lanes and more than one leaf, so the sort had work to do.
    /// </remarks>
    [Test]
    public void Build_TheSameViewTwice_AllocatesNothingTheSecondTimeAndBuildsTheSame()
    {
        List<BspDetailProp> props = [];

        for (int leaf = 0; leaf < 6; leaf++)
        {
            for (int item = 0; item < 6; item++)
            {
                props.Add(Prop(leaf, (leaf * 64f) + (item * 9f), item * 7f, item));
            }
        }

        DetailFade fade = DetailFade.For(distance: 1000f, fade: 100f);
        (float X, float Y, float Z) eye = (-50f, 10f, 20f);
        (float X, float Y, float Z) forward = (1f, 0f, 0f);
        List<DetailSpriteVertex> first = [];
        List<DetailSpriteVertex> second = new(4096);

        BspDetailSprite[] sprites = [Rectangle];

        DetailSprites.Frame built = DetailSprites.Build(props, sprites, eye, fade, first, forward: forward);
        DetailSprites.Build(props, sprites, eye, fade, second, forward: forward);
        second.Clear();

        long before = GC.GetAllocatedBytesForCurrentThread();
        DetailSprites.Build(props, sprites, eye, fade, second, forward: forward);
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

        second.ShouldBe(first);
        built.Built.ShouldBe(36);
        built.Fast.ShouldBeInRange(1, 35);
        allocated.ShouldBe(0);
    }

    private static BspDetailProp Prop(int leaf, float x, float y, int item) =>
        new(
            Origin: (x, y, 0f),
            Angles: (0f, item * 40f, 0f),
            DetailModel: 0,
            Leaf: leaf,
            Type: DetailPropType.Sprite,
            Orientation: item < 3 ? 2 : item - 3,
            SwayAmount: 0,
            ShapeAngle: 0,
            ShapeSize: 0,
            Scale: 1f,
            Lighting: (100f, 120f, 140f),
            Flipped: item % 2 == 1);
}
