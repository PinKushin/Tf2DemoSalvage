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
