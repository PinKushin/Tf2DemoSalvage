using System.Collections.Generic;
using System.IO;
using System.Linq;

using Tf2DemoSalvage.Content.Bsp;
using Tf2DemoSalvage.SdkReference;

namespace Tf2DemoSalvage.Rendering.Tests;

/// <summary>
/// The B597 control (<c>TF2VIEW_MODEL_VERTS</c>) wired into the model draw and the model decal draw, on a real device.
/// </summary>
/// <remarks>
/// **Two instruments, both the production ones**: <see cref="SubmitCounters.Draws"/>, which is what <c>--measure</c>
/// counts per pass, and a pipeline statistics query's <c>IAVertices</c>, which is what the GPU was actually handed — the
/// quantity the control exists to change. The model has one EMPTY batch beside a six-vertex one, because the default
/// path draws a zero-vertex batch too and a cap must not be what decides whether it does.
///
/// **Non-parallelizable because the draw counter is process-wide**: a sibling drawing beside these would add its own.
/// </remarks>
[NonParallelizable]
public sealed class ModelVertexClampRenderTests
{
    [Test]
    public void DrawModel_NoCapWithAnEmptyBatch_DrawsBothBatchesWhole()
    {
        (long draws, ulong vertices) = DrawModel(null) ?? Skip();

        draws.ShouldBe(2, "the empty batch is drawn too with no cap, as it was before the control");
        vertices.ShouldBe(6ul);
    }

    [Test]
    public void DrawModel_CapThree_DrawsBothBatchesWithThreeVertices()
    {
        (long draws, ulong vertices) = DrawModel(3) ?? Skip();

        draws.ShouldBe(2, "only a cap of zero suppresses a draw, so V3 counts the same draws as N");
        vertices.ShouldBe(3ul);
    }

    [Test]
    public void DrawModel_CapZero_DrawsNothing()
    {
        (long draws, ulong vertices) = DrawModel(0) ?? Skip();

        draws.ShouldBe(0);
        vertices.ShouldBe(0ul);
    }

    [Test]
    public void DrawModelDecals_NoCap_DrawsEveryRunWhole()
    {
        (long draws, ulong vertices) = DrawDecals(null) ?? Skip();

        draws.ShouldBe(2);
        vertices.ShouldBe(12ul);
    }

    [Test]
    public void DrawModelDecals_CapThree_DrawsEveryRunWithThreeVertices()
    {
        (long draws, ulong vertices) = DrawDecals(3) ?? Skip();

        draws.ShouldBe(2);
        vertices.ShouldBe(6ul);
    }

    [Test]
    public void DrawModelDecals_CapZero_DrawsNothing()
    {
        (long draws, ulong vertices) = DrawDecals(0) ?? Skip();

        draws.ShouldBe(0);
        vertices.ShouldBe(0ul);
    }

    /// <summary>A cap set between the model and its decals is the one the decals draw under.</summary>
    [Test]
    public void DrawModelDecals_CapZeroSetAfterTheModel_DrawsNothing()
    {
        (long draws, ulong vertices) = DrawDecals(0, capAfterModel: true) ?? Skip();

        draws.ShouldBe(0);
        vertices.ShouldBe(0ul);
    }

    /// <remarks>
    /// **The cloak pass draws inside the model pass too** (`DrawCloakPass`). A third cloaked, `spy_red` draws its standard
    /// pass (at or below 4/9) and then its cloak pass (strictly inside 0 and 1): two draws of the six-vertex batch.
    ///
    /// **Plus the frame copy before it, which the cap does not touch**: `UpdateRefractTexture`'s copy is a `Blit`, one
    /// draw of a three-vertex full-screen triangle (`WorldRenderer.Water.cs`, `Blit`). It is a copy of the frame, not the
    /// model's geometry, so every case below carries its one draw and three vertices.
    /// </remarks>
    [Test]
    public void DrawModel_CloakedNoCap_DrawsBothPassesWhole()
    {
        (long draws, ulong vertices) = DrawCloaked(null) ?? Skip();

        draws.ShouldBe(1 + 2);
        vertices.ShouldBe(3ul + 12ul);
    }

    [Test]
    public void DrawModel_CloakedCapThree_DrawsBothPassesWithThreeVertices()
    {
        (long draws, ulong vertices) = DrawCloaked(3) ?? Skip();

        draws.ShouldBe(1 + 2);
        vertices.ShouldBe(3ul + 6ul);
    }

    [Test]
    public void DrawModel_CloakedCapZero_DrawsOnlyTheFrameCopy()
    {
        (long draws, ulong vertices) = DrawCloaked(0) ?? Skip();

        draws.ShouldBe(1);
        vertices.ShouldBe(3ul);
    }

    /// <remarks>
    /// **The B222 ledger reports what was SUBMITTED**, and is read from its own line rather than recomputed: the line is
    /// written only when the ledger changes, so an uncapped draw followed by a capped one must report six becoming three.
    /// </remarks>
    [Test]
    public void DrawModel_CapThreeAfterAnUncappedDraw_ReportsThreeVerticesSubmitted()
    {
        using RecordingLoggerFactory loggers = new();
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16, loggers);

        if (target is null || Assets is not { } assets)
        {
            Skip();
            return;
        }

        Draw(target, assets);
        target.ModelVertexCap = 3;
        Draw(target, assets);

        loggers.From("render").Where(line => line.StartsWith("posed submitted:", System.StringComparison.Ordinal))
            .ShouldHaveSingleItem()
            .ShouldContain("2 batches/6 vertices -> 2 batches/3 vertices");
    }

    /// <summary>One model draw under <paramref name="cap"/>, counted; null when there is no device or no game.</summary>
    private static (long Draws, ulong Vertices)? DrawModel(int? cap)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null || Assets is not { } assets)
        {
            return null;
        }

        target.ModelVertexCap = cap;

        long before = SubmitCounters.Draws;
        ulong vertices = target.InputVertices(() => Draw(target, assets));

        return (SubmitCounters.Draws - before, vertices);
    }

    /// <summary>Two six-corner decal runs on the model under <paramref name="cap"/>, counted apart from the model.</summary>
    /// <param name="cap">The cap.</param>
    /// <param name="capAfterModel">Draw the model with no cap and set <paramref name="cap"/> only before the decals.</param>
    private static (long Draws, ulong Vertices)? DrawDecals(int? cap, bool capAfterModel = false)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null || Assets is not { } assets)
        {
            return null;
        }

        target.ModelVertexCap = capAfterModel ? null : cap;
        Draw(target, assets);
        target.ModelVertexCap = cap;

        List<WorldVertex> corners = [.. Quad(), .. Quad()];

        long before = SubmitCounters.Draws;
        ulong vertices = target.InputVertices(() =>
            target.DrawPosedModelDecals(corners, [new WorldBatch(0, 0, 6), new WorldBatch(0, 6, 6)]));

        return (SubmitCounters.Draws - before, vertices);
    }

    /// <summary>A six-vertex batch of <c>spy_red</c>, a third cloaked, under <paramref name="cap"/>, counted.</summary>
    private static (long Draws, ulong Vertices)? DrawCloaked(int? cap)
    {
        using OffscreenTarget? target = OffscreenTarget.TryCreate(16, 16);

        if (target is null || Assets is null)
        {
            return null;
        }

        MapAssets assets = MapCache.Load(entityModels: [SpyModel]);
        int spyRed = Enumerable.Range(0, assets.Materials.Count)
            .First(at => assets.Materials[at].Name.EndsWith("spy/spy_red", System.StringComparison.OrdinalIgnoreCase));

        assets.Textures[spyRed]!.Value.Cloak.ShouldNotBeNull("the control: spy_red has a cloak pass to draw");
        target.ModelVertexCap = cap;

        long before = SubmitCounters.Draws;
        ulong vertices = target.InputVertices(() =>
            target.DrawModelPose(
                Quad(),
                [new WorldBatch(spyRed, 0, 6)],
                new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix(),
                [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f],
                assets,
                bothSides: true,
                cloak: new CloakBind(0.3f, 0.3f)));

        return (SubmitCounters.Draws - before, vertices);
    }

    private const string SpyModel = "models/player/spy.mdl";

    /// <summary>The model: an empty batch, then six vertices.</summary>
    private static void Draw(OffscreenTarget target, MapAssets assets) =>
        target.DrawModelPose(
            Quad(),
            [new WorldBatch(0, 0, 0), new WorldBatch(0, 0, 6)],
            new FreeCamera { Origin = (0f, -300f, 0f), Angles = (0f, 90f, 0f), Aspect = 1f }.ToMatrix(),
            [1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f, 0f, 0f, 0f, 0f, 1f],
            assets,
            bothSides: true);

    private static List<WorldVertex> Quad() =>
    [
        new(-64f, 0f, -64f, 0f, 0f, 0f, 0f, 0f),
        new(64f, 0f, -64f, 1f, 0f, 0f, 0f, 0f),
        new(64f, 0f, 64f, 1f, 1f, 0f, 0f, 0f),
        new(-64f, 0f, -64f, 0f, 0f, 0f, 0f, 0f),
        new(64f, 0f, 64f, 1f, 1f, 0f, 0f, 0f),
        new(-64f, 0f, 64f, 0f, 1f, 0f, 0f, 0f),
    ];

    private static (long, ulong) Skip()
    {
        Assert.Ignore("no Direct3D, or the map or the game is not installed");
        return default;
    }

    private static MapAssets? Assets =>
        GameInstall.Root is { } tf && File.Exists(Path.Combine(tf, "maps", "cp_process_final.bsp"))
            ? MapCache.With().Assets
            : null;
}
