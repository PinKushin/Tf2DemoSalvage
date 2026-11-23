using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>An offscreen target releases its device: nothing it made outlives it (2026-10-18).</summary>
/// <remarks>
/// **Why this is a test and not a measurement**: a child object left unreleased keeps the device alive, and the device
/// keeps every texture uploaded through it — the render suite's private bytes climbed to 26 GB with the managed heap at
/// two to six. Each path a test drives through a target is asserted here to leave the device's count at zero.
/// </remarks>
public sealed class OffscreenTargetReleaseTests
{
    private static readonly float[] Identity = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f];

    /// <remarks>The control: a target that drew nothing is released whole, so the instrument reads zero when it should.</remarks>
    [Test]
    public void Dispose_ATargetThatDrewNothing_LeavesNoDeviceReference()
    {
        OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(8, 8), "no Direct3D on this machine");
        target.Clear(0f, 0f, 0f);
        _ = target.PixelAt(0, 0);

        target.Dispose();

        target.DeviceReferencesLeft.ShouldBe(0u);
    }

    /// <remarks>Lines through the point renderer.</remarks>
    [Test]
    public void Dispose_AfterDrawingLines_LeavesNoDeviceReference()
    {
        OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(8, 8), "no Direct3D on this machine");
        target.DrawLines([((-1f, 0f), (1f, 0f))]);

        target.Dispose();

        target.DeviceReferencesLeft.ShouldBe(0u);
    }

    /// <remarks>A world draw through the world renderer, with a real map's textures.</remarks>
    [Test]
    public void Dispose_AfterDrawingTheWorld_LeavesNoDeviceReference()
    {
        MapAssets assets = MapCache.Load();
        OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(8, 8), "no Direct3D on this machine");
        List<WorldVertex> quad =
        [
            new(-1f, -1f, 0.5f, 0f, 0f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(1f, 1f, 0.5f, 1f, 1f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(1f, -1f, 0.5f, 1f, 0f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
        ];
        target.DrawWorld(quad, [new WorldBatch(0, 0, quad.Count)], Identity, assets);

        target.Dispose();

        target.DeviceReferencesLeft.ShouldBe(0u);
    }

    /// <remarks>
    /// **The face outlines go with the geometry, on a second upload too** (B803 review 1, Grok): a world drawn twice under
    /// `mat_wireframe 3` uploads its outline twice, and a brush entity drawn twice (the target clears its models between
    /// poses) uploads its own twice. `UploadGeometry` releases the last world's outline first (<c>ReleaseGeometry</c>), and a
    /// model's is made only beside a model buffer the upload has not got, both cleared together, so neither is overwritten
    /// while held. This is that claim's control: a held outline keeps the device's count above zero.
    /// </remarks>
    [Test]
    public void Dispose_AfterDrawingTheWorldAndABrushEntityTwiceUnderWireframeThree_LeavesNoDeviceReference()
    {
        OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(8, 8), "no Direct3D on this machine");
        MapAssets assets = MapAssets.ForMaterials(
            [
                new MapTexture(4, 4, 4, 4, TextureImage.Rgba(new byte[64]), IsTransparent: false),
                new MapTexture(4, 4, 4, 4, TextureImage.Rgba(new byte[64]), IsTransparent: false),
            ],
            ["UnlitGeneric", "Wireframe"],
            engineWireframes: new Dictionary<string, WireframeMaterial> { [MapAssets.WorldWireframePath] = new(1, IgnoresZ: true) });
        List<WorldVertex> quad =
        [
            new(-1f, -1f, 0.5f, 0f, 0f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(1f, 1f, 0.5f, 1f, 1f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(1f, -1f, 0.5f, 1f, 0f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
        ];

        target.Wireframe = 3;

        for (int draw = 0; draw < 2; draw++)
        {
            target.DrawWorld(quad, [new WorldBatch(0, 0, quad.Count)], Identity, assets);
            target.DrawModelPose(quad, [new WorldBatch(0, 0, quad.Count)], Identity, Identity, assets, modelPath: "*1");
        }

        target.Dispose();

        target.DeviceReferencesLeft.ShouldBe(0u);
    }

    /// <remarks>
    /// **A brush entity that is not whole triangles draws, keeps no outline, and releases cleanly** (B803 review 3, Fable):
    /// its upload stores a null outline handle (review 2), which the release skips. Four vertices of `*1` drawn under 3,
    /// then the target released: no exception and nothing left on the device.
    /// </remarks>
    [Test]
    public void Dispose_AfterDrawingABrushEntityOfAPartialTriangleUnderWireframeThree_LeavesNoDeviceReference()
    {
        OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(8, 8), "no Direct3D on this machine");
        MapAssets assets = MapAssets.ForMaterials(
            [
                new MapTexture(4, 4, 4, 4, TextureImage.Rgba(new byte[64]), IsTransparent: false),
                new MapTexture(4, 4, 4, 4, TextureImage.Rgba(new byte[64]), IsTransparent: false),
            ],
            ["UnlitGeneric", "Wireframe"],
            engineWireframes: new Dictionary<string, WireframeMaterial> { [MapAssets.WorldWireframePath] = new(1, IgnoresZ: true) });
        List<WorldVertex> corners =
        [
            new(-1f, -1f, 0.5f, 0f, 0f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(1f, 1f, 0.5f, 1f, 1f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(1f, -1f, 0.5f, 1f, 0f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(-1f, 1f, 0.5f, 0f, 1f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
        ];

        target.Wireframe = 3;
        target.DrawModelPose(corners, [new WorldBatch(0, 0, 3)], Identity, Identity, assets, modelPath: "*1");

        target.Dispose();

        target.DeviceReferencesLeft.ShouldBe(0u);
    }

    /// <remarks>A skinned model draw, which makes the bone buffer on first use.</remarks>
    [Test]
    public void Dispose_AfterDrawingASkinnedModel_LeavesNoDeviceReference()
    {
        MapAssets assets = MapCache.Load();
        OffscreenTarget target = Skip.Unless(OffscreenTarget.TryCreate(8, 8), "no Direct3D on this machine");
        List<WorldVertex> quad =
        [
            new(-1f, -1f, 0.5f, 0f, 0f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(1f, 1f, 0.5f, 1f, 1f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
            new(1f, -1f, 0.5f, 1f, 0f, 0.5f, 0.5f, 0f, 1f, 1f, 1f),
        ];
        float[] bone = [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f];
        target.DrawModelPose(quad, [new WorldBatch(0, 0, quad.Count)], Identity, Identity, assets, bones: [bone]);

        target.Dispose();

        target.DeviceReferencesLeft.ShouldBe(0u);
    }
}
