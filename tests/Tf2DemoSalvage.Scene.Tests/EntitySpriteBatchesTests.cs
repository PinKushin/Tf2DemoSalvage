using System.Collections.Generic;
using System.Numerics;

using Tf2DemoSalvage.Content.Assets;
using Tf2DemoSalvage.Core.Scene;

namespace Tf2DemoSalvage.Scene.Tests;

/// <summary>
/// That the sprite pass draws what each sprite's MATERIAL asks for (B390).
/// </summary>
/// <remarks>
/// **The output-level test B378 did not have.** `EntitySprites` was conformance-tested function by
/// function and every branch of `GetSpriteAxes` passed — while the one caller passed the literal
/// <c>SPR_VP_PARALLEL_UPRIGHT</c> for every sprite, so four of the five branches were reachable only
/// from their own tests (`docs/memory/output-level-assertion-or-it-is-not-done.md`).
///
/// **Straight down is where the orientations disagree about whether to draw at all.** The upright
/// kinds build their basis from a cross product against world up and REFUSE within one degree of it;
/// the viewplane kinds take the view's own axes and never refuse. So a glow seen from above is drawn
/// or skipped according to nothing but its orientation — a count, not a picture, and exact.
/// </remarks>
public sealed class EntitySpriteBatchesTests
{
    private const string Glow = "materials/sprites/light_glow03.vmt";

    /// <remarks>
    /// `light_glow03` as it ships: `"$spriteorientation" "vp_parallel"`. Drawn from overhead, because
    /// nothing about a viewplane-parallel sprite depends on where the camera points.
    /// </remarks>
    [Test]
    public void Build_AViewplaneParallelGlowSeenFromStraightAbove_IsDrawn()
    {
        EntitySpriteBatches sprites = new();

        IReadOnlyList<ParticleBatch> batches = Overhead(sprites, SpriteOrientation.Parallel);

        sprites.Drawn.ShouldBe(1);
        batches.Count.ShouldBe(1);
        batches[0].Corners.Count.ShouldBe(6, "two triangles");
    }

    /// <remarks>
    /// **The control, and the reason the test above means anything.** The same glow with the DEFAULT
    /// orientation is refused from the same place — <c>if ((dot &gt; 0.999848f) || (dot &lt;
    /// -0.999848f)) return;</c> — so the only difference between the two runs is which orientation
    /// reached `GetSpriteAxes`. A pass that ignored the material would give this answer for both.
    /// </remarks>
    [Test]
    public void Build_AnUprightGlowSeenFromStraightAbove_IsRefused()
    {
        EntitySpriteBatches sprites = new();

        Overhead(sprites, SpriteOrientation.ParallelUpright).ShouldBeEmpty();

        sprites.Drawn.ShouldBe(0);
        sprites.Skipped.ShouldBe(1);
    }

    /// <remarks>
    /// **The origin reaches the corners.** `[ 0.50 0.00 ]` puts the quad's top edge on the sprite's
    /// origin, so every corner is at or below it along the view's up, where a centred quad would put
    /// half of them above.
    /// </remarks>
    [Test]
    public void Build_AGlowWhoseOriginIsItsTopEdge_HangsBelowIt()
    {
        EntitySpriteBatches sprites = new();

        IReadOnlyList<ParticleBatch> batches = Overhead(
            sprites, SpriteOrientation.Parallel, SpriteExtents.Of(128, 128, (0.5f, 0f)));

        // Looking straight down with the view's up along +Y, "above the origin" is +Y.
        batches[0].Corners.ShouldAllBe(corner => corner.Y <= 0f);
        batches[0].Corners.ShouldContain(corner => corner.Y < -1f);
    }

    /// <summary>One world glow at the origin, drawn by a camera 500 units above it looking down.</summary>
    private static IReadOnlyList<ParticleBatch> Overhead(
        EntitySpriteBatches sprites, SpriteOrientation orientation, SpriteExtents? extents = null)
    {
        EngineSprite glow = new(
            new ParticleMaterial(
                new MapTexture(128, 128, TextureImage.None, IsTransparent: true),
                [],
                SpriteBlend.Additive),
            128,
            128,
            orientation,
            extents ?? SpriteExtents.Of(128, 128, origin: null));

        SceneProp prop = new(
            EntityIndex: 42,
            ModelPath: Glow,
            Kind: SceneModelKind.Sprite,
            Pose: new ScenePose { RenderMode = RenderModes.WorldGlow, Sprite = SceneSprite.Default });

        return sprites.Build(
            [prop],
            eye: new Vector3(0f, 0f, 500f),
            viewRight: Vector3.UnitX,
            viewUp: Vector3.UnitY,
            viewForward: -Vector3.UnitZ,
            sprites: new Dictionary<string, EngineSprite>(System.StringComparer.OrdinalIgnoreCase)
            {
                [Glow] = glow,
            },
            visible: _ => true);
    }
}
