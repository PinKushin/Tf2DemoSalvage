using System.Collections.Generic;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>The beam and trail strips' corners grouped by material and pass, built without garbage (B599).</summary>
/// <remarks>
/// **The census's two largest effect-entity sites were here** (`measurements/2026-10-29-allocation-census.md`): a `foreach`
/// over the interface-typed corner list and one over the pass list each boxed an enumerator per strip per frame, and a
/// non-Sprite material's single pass was a new list each time. Warmed twice (the per-material lists are made on first
/// use, and the Sprite-shader case's SECOND call still made 88 bytes once — a one-time cost, not per call: a third call
/// makes none), cleared, then added again: the same corners, nothing allocated.
/// </remarks>
public sealed class SpriteStripBatchesTests
{
    private const string Path = "sprites/strip.vmt";

    [TestCase("Sprite", RenderModes.TransAlphaAdd, 2, TestName = "Add_ASpriteShaderStripAtTransAlphaAddWarm_AllocatesNothing")]
    [TestCase("UnlitGeneric", RenderModes.TransAdd, 1, TestName = "Add_ANonSpriteShaderStripWarm_AllocatesNothing")]
    public void Add_AStripWarm_AllocatesNothing(string shader, int renderMode, int passes)
    {
        SpriteStripBatches strips = new();
        EngineSprite sprite = Sprite(shader);
        List<DetailSpriteVertex> corners = Corners();

        strips.Add(Path, sprite, renderMode, corners);
        strips.Clear();
        strips.Add(Path, sprite, renderMode, corners);
        strips.Clear();

        long before = System.GC.GetAllocatedBytesForCurrentThread();
        strips.Add(Path, sprite, renderMode, corners);
        long bytes = System.GC.GetAllocatedBytesForCurrentThread() - before;

        strips.Corners.ShouldBe(passes * corners.Count, "the control: every pass took every corner");
        bytes.ShouldBe(0L);
    }

    /// <summary>
    /// The last-list shortcut must key on the pass as well as the path: one material at two render modes is two lists, and
    /// a strip of another material between two of the first must not land in the second's list.
    /// </summary>
    [Test]
    public void Add_AlternatingMaterialsAndModes_GroupsEachStripUnderItsOwnMaterialAndPass()
    {
        SpriteStripBatches strips = new();
        EngineSprite sprite = Sprite("Sprite");
        const string other = "sprites/other.vmt";
        List<DetailSpriteVertex> corners = Corners();

        strips.Add(Path, sprite, RenderModes.TransAdd, corners);
        strips.Add(Path, sprite, RenderModes.Normal, corners);
        strips.Add(other, sprite, RenderModes.TransAdd, corners);
        strips.Add(Path, sprite, RenderModes.TransAdd, corners);

        // An equal path in another string instance misses the shortcut and must still find the same list.
        strips.Add(new string(Path.ToCharArray()), sprite, RenderModes.Normal, corners);

        Dictionary<string, EngineSprite> sprites = new() { [Path] = sprite, [other] = sprite };
        List<(SpriteBlend Blend, int Corners)> batches = [];

        foreach (ParticleBatch batch in strips.Batches(sprites))
        {
            batches.Add((batch.Material.Blend, batch.Corners.Count));
        }

        // Path additive: two strips; path normal: two (one through the copied string); other additive: one.
        batches.ShouldBe(
            [(EntitySprites.PassesFor(RenderModes.TransAdd)[0].Blend, 8),
             (EntitySprites.PassesFor(RenderModes.Normal)[0].Blend, 8),
             (EntitySprites.PassesFor(RenderModes.TransAdd)[0].Blend, 4)],
            ignoreOrder: true);
        strips.Corners.ShouldBe(20);
    }

    private static List<DetailSpriteVertex> Corners() =>
    [
        new(0f, 0f, 0f, 0f, 0f, 1f, 1f, 1f, 1f, 0f, 0f, 0f),
        new(1f, 0f, 0f, 1f, 0f, 1f, 1f, 1f, 1f, 1f, 0f, 0f),
        new(1f, 1f, 0f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 1f, 0f),
        new(0f, 1f, 0f, 0f, 1f, 1f, 1f, 1f, 1f, 0f, 1f, 0f),
    ];

    private static EngineSprite Sprite(string shader)
    {
        MapTexture sheet = new(64, 64, 64, 64, TextureImage.None, IsTransparent: true);

        return new EngineSprite(
            new ParticleMaterial(sheet, [], SpriteBlend.Translucent),
            64,
            64,
            SpriteOrientation.Parallel,
            SpriteExtents.Of(64, 64, origin: null),
            (1f, 1f, 1f, 1f),
            IgnoresVertexColors: true,
            Shader: shader,
            Modulation: (1f, 1f, 1f, 1f),
            VertexColour: true,
            VertexAlpha: true);
    }
}
