using System;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>Everything that runs under the TF2VIEW_MODEL_CULL experiment, which a renderer reads once when it is made.</summary>
/// <remarks>
/// **Its own fixture because the variable is process-wide**: set inside a test that shares a fixture, or a run, with others,
/// it would change their culling too. Each test sets it, makes its target, and puts it back.
///
/// **What the overrides do to the rasteriser states**: `none` makes the model cull's description the both-sided one, and
/// `front` makes it the mirror's. D3D11 returns ONE object for an identical description, so two draw states, and their
/// polygon-offset twins, are the same object (B635 review 5). Overwriting the twin's dictionary entry left one reference
/// unreleased; the release tests below are what can see it.
/// </remarks>
[NonParallelizable]
public sealed class ModelCullOverrideReleaseTests
{
    private const string Variable = "TF2VIEW_MODEL_CULL";

    [TestCase("none")]
    [TestCase("front")]
    public void Dispose_AfterDrawingUnderAModelCullOverride_LeavesNoDeviceReference(string cull)
    {
        string? before = Environment.GetEnvironmentVariable(Variable);

        Environment.SetEnvironmentVariable(Variable, cull);

        try
        {
            OffscreenTarget target = OffscreenTarget.TryCreate(16, 16) ?? Ignored();

            target.DrawWorld(
                ModulateToneMapConformanceTests.Quad(1f),
                [new WorldBatch(ModulateToneMapConformanceTests.Lit, 0, 6)],
                ModulateToneMapConformanceTests.Camera(),
                ModulateToneMapConformanceTests.Assets);
            target.DrawModelPose(
                ModulateToneMapConformanceTests.Quad(1f),
                [new WorldBatch(ModulateToneMapConformanceTests.DecalModulateDark, 0, 6)],
                ModulateToneMapConformanceTests.Camera(),
                ModulateToneMapConformanceTests.Identity,
                ModulateToneMapConformanceTests.Assets);

            target.Dispose();

            target.DeviceReferencesLeft.ShouldBe(0u, $"under {Variable}={cull}");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, before);
        }
    }

    /// <remarks>
    /// **The offset keeps CullFor's mode, including the TF2VIEW_MODEL_CULL experiment** (B635 review 4): with the override
    /// set to none, a DecalModulate model seen from behind draws, as any other model's back face does. Moved here from
    /// <see cref="ModulateToneMapConformanceTests"/> in review 5, with the variable.
    /// </remarks>
    [Test]
    public void DrawModel_DecalModulateSeenFromBehindUnderModelCullNone_StillDraws()
    {
        string? before = Environment.GetEnvironmentVariable(Variable);

        Environment.SetEnvironmentVariable(Variable, "none");

        try
        {
            using OffscreenTarget target = OffscreenTarget.TryCreate(16, 16) ?? Ignored();
            float surface = ModulateToneMapConformanceTests.Surface;

            target.Clear(surface, surface, surface);

            (int, int, int) under = target.PixelAt(8, 8);

            target.DrawModelPose(
                ModulateToneMapConformanceTests.Quad(1f, away: true),
                [new WorldBatch(ModulateToneMapConformanceTests.DecalModulateDark, 0, 6)],
                ModulateToneMapConformanceTests.Camera(),
                ModulateToneMapConformanceTests.Identity,
                ModulateToneMapConformanceTests.Assets,
                toneMapScale: 1f);

            (int, int, int) drawn = target.PixelAt(8, 8);

            drawn.Item2.ShouldBeLessThan(under.Item2 - 40, $"the scorch seen from behind under model cull none drew {drawn} over {under}");
        }
        finally
        {
            Environment.SetEnvironmentVariable(Variable, before);
        }
    }

    private static OffscreenTarget Ignored() => throw new IgnoreException("no Direct3D device on this machine");
}
